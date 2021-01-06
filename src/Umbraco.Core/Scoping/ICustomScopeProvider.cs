using System;
using System.Data;
using Umbraco.Core.Events;
using Umbraco.Core.Persistence;

#if DEBUG_SCOPES
using System.Collections.Generic;
#endif

namespace Umbraco.Core.Scoping
{
    /// <summary>
    /// Provides scopes.
    /// </summary>
    public interface ICustomScopeProvider : IScopeProvider
    {    
    }
}
