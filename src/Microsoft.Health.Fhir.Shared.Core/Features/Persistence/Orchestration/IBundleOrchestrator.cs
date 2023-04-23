﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public interface IBundleOrchestrator
    {
        bool IsEnabled { get; }

        IBundleOrchestratorOperation CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources);

        bool CompleteOperation(IBundleOrchestratorOperation operation);
    }
}