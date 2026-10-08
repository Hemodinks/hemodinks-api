using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformTeamRequestHandler
{
        private readonly IPlatformTeamDbContext context;
        private readonly IPinHasher pinHasher;
        private readonly PlatformAuditRecorder auditService;

        public ClinicaPlatformTeamRequestHandler(
            IPlatformTeamDbContext context,
            IPinHasher pinHasher,
            PlatformAuditRecorder auditService)
        {
            this.context = context;
            this.pinHasher = pinHasher;
            this.auditService = auditService;
        }
}
