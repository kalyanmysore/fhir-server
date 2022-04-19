﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class AzureAccessTokenClientInitializerTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private ILogger<AzureAccessTokenClientInitializer> _logger;

        private AzureAccessTokenClientInitializer _azureAccessTokenClientInitializer;

        public AzureAccessTokenClientInitializerTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _logger = Substitute.For<ILogger<AzureAccessTokenClientInitializer>>();

            _azureAccessTokenClientInitializer = new AzureAccessTokenClientInitializer(optionsExportConfig, _logger);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [Theory]
        public async Task GivenNullOrEmptyStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [InlineData("randomUri")]
        [InlineData("https://")]
        [Theory]
        public async Task GivenInvalidStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenAbleToGetAccessToken_WhenGetAuthorizedClientAsync_ThenClientIsReturned()
        {
            _exportJobConfiguration.StorageAccountUri = "https://localhost/storage";

            BlobServiceClient client = await _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None);

            Assert.NotNull(client);
        }
    }
}
