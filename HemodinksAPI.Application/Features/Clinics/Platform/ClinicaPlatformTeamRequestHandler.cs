using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformTeamRequestHandler
{
        private readonly IPlatformTeamDbContext context;
        private readonly IPasswordHasher passwordHasher;
        private readonly PlatformAuditRecorder auditService;

        public ClinicaPlatformTeamRequestHandler(
            IPlatformTeamDbContext context,
            IPasswordHasher passwordHasher,
            PlatformAuditRecorder auditService)
        {
            this.context = context;
            this.passwordHasher = passwordHasher;
            this.auditService = auditService;
        }
}
