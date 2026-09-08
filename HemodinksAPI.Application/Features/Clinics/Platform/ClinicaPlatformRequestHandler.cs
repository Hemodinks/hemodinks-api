using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Utils;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        private const string DuplicateCnpjMessage = "Este CNPJ ja esta cadastrado em outra clinica";
        private const string UniqueCnpjIndexName = "IX_Clinicas_Cnpj";

        private readonly IPlatformClinicDbContext context;
        private readonly IDataExecutionStrategy executionStrategy;
        private readonly IDataTransactionManager transactionManager;
        private readonly IPasswordHasher passwordHasher;
        private readonly IProfilePhotoStorage photoStorage;
        private readonly PlatformAuditRecorder auditService;
        private readonly IValidator<CreateClinicaRequest> createValidator;
        private readonly IValidator<UpdateClinicaRequest> updateValidator;

        public ClinicaPlatformRequestHandler(
            IPlatformClinicDbContext context,
            IDataExecutionStrategy executionStrategy,
            IDataTransactionManager transactionManager,
            IPasswordHasher passwordHasher,
            IProfilePhotoStorage photoStorage,
            PlatformAuditRecorder auditService,
            IValidator<CreateClinicaRequest> createValidator,
            IValidator<UpdateClinicaRequest> updateValidator)
        {
            this.context = context;
            this.executionStrategy = executionStrategy;
            this.transactionManager = transactionManager;
            this.passwordHasher = passwordHasher;
            this.photoStorage = photoStorage;
            this.auditService = auditService;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
        }

        private static bool IsCnpjUniqueConstraintViolation(DbUpdateException exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current.Message.Contains(UniqueCnpjIndexName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
}
