// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Mvc.ModelBinding
{
    /// <summary>
    /// Represents a marker used to identify a particular binder applies to an artifact.
    /// </summary>
    public interface IBinderMarker
    {
        /// <summary>
        /// Set this to true to force the binding to occur.
        /// </summary>
        bool ForceBind { get; set; }
    }
}