﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Registration
{
    public interface IFhirRuntimeConfiguration
    {
        bool IsSelectiveSearchParameterSupported { get; }
    }
}