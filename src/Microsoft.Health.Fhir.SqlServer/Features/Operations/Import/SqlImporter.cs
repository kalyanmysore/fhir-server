﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlImporter : IImporter
    {
        private readonly SqlServerFhirModel _model;
        private readonly IFhirDataStore _store;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private readonly IImportErrorSerializer _importErrorSerializer;
        private readonly ILogger<SqlImporter> _logger;

        public SqlImporter(
            IFhirDataStore store,
            SqlServerFhirModel model,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _importErrorSerializer = EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            _importTaskConfiguration = EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig)).Value.Import;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, ImportMode importMode, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting import to SQL data store...");

                await _model.EnsureInitialized();

                long succeededCount = 0;
                long failedCount = 0;
                long processedBytes = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();
                var resourceBuffer = new List<ImportResource>();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.TransactionSize)
                    {
                        continue;
                    }

                    ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, importMode, cancellationToken, ref succeededCount, ref failedCount, ref processedBytes);
                }

                ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, importMode, cancellationToken, ref succeededCount, ref failedCount, ref processedBytes);

                return await UploadImportErrorsAsync(importErrorStore, succeededCount, failedCount, importErrorBuffer.ToArray(), currentIndex, processedBytes, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import to SQL data store completed.");
            }
        }

        private void ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, ImportMode importMode, CancellationToken cancellationToken, ref long succeededCount, ref long failedCount, ref long processedBytes)
        {
            var errorResources = resources.Where(r => !string.IsNullOrEmpty(r.ImportError));
            var goodResources = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            List<ImportResource> dedupped;
            IEnumerable<ImportResource> merged;
            if (importMode == ImportMode.InitialLoad)
            {
                var inputDedupped = goodResources.GroupBy(_ => _.ResourceWrapper.ToResourceKey(true)).Select(_ => _.OrderBy(_ => _.ResourceWrapper.LastModified).First()).ToList();
                var existing = new HashSet<ResourceKey>(GetAsync(inputDedupped.Select(_ => _.ResourceWrapper.ToResourceKey(true)).ToList(), cancellationToken).Result.Select(_ => _.ToResourceKey(true)));
                dedupped = inputDedupped.Where(i => !existing.TryGetValue(i.ResourceWrapper.ToResourceKey(true), out _)).ToList();
                merged = MergeResourcesAsync(dedupped, cancellationToken).Result;
            }
            else
            {
                var inputDedupped = goodResources.GroupBy(_ => (_.ResourceWrapper.ToResourceKey(true), _.ResourceWrapper.LastModified)).Select(_ => _.First()).ToList();

                // 2 paths:
                // 1 - if versions were specified on input then dups need to be checked within input and database
                var inputDeduppedWithVersions = inputDedupped.Where(_ => _.KeepVersion).GroupBy(_ => _.ResourceWrapper.ToResourceKey()).Select(_ => _.First()).ToList();
                var existing = new HashSet<ResourceKey>(GetAsync(inputDeduppedWithVersions.Select(_ => _.ResourceWrapper.ToResourceKey()).ToList(), cancellationToken).Result.Select(_ => _.ToResourceKey()));
                dedupped = inputDeduppedWithVersions.Where(i => !existing.TryGetValue(i.ResourceWrapper.ToResourceKey(), out _)).ToList();
                var mergedWithVersions = MergeResourcesAsync(dedupped, cancellationToken).Result;

                // 2 - if versions were not specified they have to be assigned as next based on union of input and database.
                // for simlicity assume that ony one unassigned version is provided for a given resource
                var inputDeduppedNoVersions = inputDedupped.Where(_ => !_.KeepVersion).GroupBy(_ => _.ResourceWrapper.ToResourceKey(true)).Select(_ => _.First());
                var mergedNoVersions = MergeResourcesAsync(inputDeduppedNoVersions, cancellationToken).Result;
                dedupped.AddRange(inputDeduppedNoVersions);
                merged = mergedWithVersions.Union(mergedNoVersions);
            }

            var dups = goodResources.Except(dedupped);

            errors.AddRange(errorResources.Select(r => r.ImportError));
            AppendDuplicateErrorsToBuffer(dups, errors);

            succeededCount += merged.Count();
            failedCount += errorResources.Count() + dups.Count();
            processedBytes += resources.Sum(_ => (long)_.Length);

            resources.Clear();
        }

        private async Task<IEnumerable<ImportResource>> MergeResourcesAsync(IEnumerable<ImportResource> resources, CancellationToken cancellationToken)
        {
            try
            {
                var input = resources.Select(_ => new ResourceWrapperOperation(_.ResourceWrapper, true, true, null, false, _.KeepVersion)).ToList();
                var result = await _store.MergeAsync(input, cancellationToken);
                return resources;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "MergeResourcesAsync failed.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        private async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> resourceKeys, CancellationToken cancellationToken)
        {
            try
            {
                return await _store.GetAsync(resourceKeys, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "GetAsync failed.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        private void AppendDuplicateErrorsToBuffer(IEnumerable<ImportResource> resources, List<string> importErrorBuffer)
        {
            foreach (var resource in resources)
            {
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportForDuplicatedResource, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
            }
        }

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeededCount, long failedCount, string[] importErrors, long lastIndex, long processedBytes, CancellationToken cancellationToken)
        {
            try
            {
                await importErrorStore.UploadErrorsAsync(importErrors, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to upload error logs.");
                throw;
            }

            var progress = new ImportProcessingProgress();
            progress.SucceededResources = succeededCount;
            progress.FailedResources = failedCount;
            progress.ProcessedBytes = processedBytes;
            progress.CurrentIndex = lastIndex + 1;

            return progress;
        }
    }
}
