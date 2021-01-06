using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Runtime.Remoting.Messaging;
using System.Web;
using Umbraco.Core.Composing;
using Umbraco.Core.Events;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;

#if DEBUG_SCOPES
using System.Linq;
#endif

namespace Umbraco.Core.Scoping
{
    /// <summary>
    /// Implements <see cref="IScopeProvider"/>.
    /// </summary>
    internal class CustomScopeProvider : ScopeProvider, ICustomScopeProvider
    {
        public CustomScopeProvider(IUmbracoDatabaseFactory databaseFactory, FileSystems fileSystems, ILogger logger) : base(databaseFactory, fileSystems, logger)
        {
        }
    }
}
