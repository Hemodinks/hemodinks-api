using HemodinksAPI.Application.Security;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class ChangePasswordCommandHandler(NewPasswordPolicy passwordPolicy, SensitiveIdentityService identity)
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResponse>
{
    public async Task<ChangePasswordResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        PasswordCommandAccess.EnsureCanChangeOwnPassword(request.CurrentUser, request.UserId);
        passwordPolicy.Validate(request.NovaSenha);
        await identity.ChangePasswordAsync(request.CurrentUser, request.UserId, request.SenhaAtual, request.NovaSenha, cancellationToken);
        return new() { Id = request.UserId, PrecisaTrocarSenha = false, Message = "Senha alterada. Entre novamente com a nova senha." };
    }
}
