using Umbraco.Core.Events;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence.Repositories;
using Umbraco.Core.Scoping;

namespace Umbraco.Core.Services.Implement
{
    /// <summary>
    /// Represents the MemberService.
    /// </summary>
    public class CustomMemberService : MemberService, ICustomMemberService
    {
        public CustomMemberService(IScopeProvider provider, ILogger logger, IEventMessagesFactory eventMessagesFactory, IMemberGroupService memberGroupService, IMediaFileSystem mediaFileSystem, IMemberRepository memberRepository, IMemberTypeRepository memberTypeRepository, IMemberGroupRepository memberGroupRepository, IAuditRepository auditRepository) : base(provider, logger, eventMessagesFactory, memberGroupService, mediaFileSystem, memberRepository, memberTypeRepository, memberGroupRepository, auditRepository)
        {
        }
    }
}
