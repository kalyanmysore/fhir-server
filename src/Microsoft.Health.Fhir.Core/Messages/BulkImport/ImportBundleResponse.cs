﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class ImportBundleResponse
    {
        public ImportBundleResponse(int count)
        {
            LoadedResources = count;
        }

        /// <summary>
        /// Number of loaded resources
        /// </summary>
        public int LoadedResources { get; }
    }
}
