using HemodinksAPI.Application.Features.Users.Queries;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

// Implementar MediatR IRequest para os comandos.
public partial class CreateUserCommand : IRequest<CreateUserResponse>
{
}

public partial class AuthenticateUserCommand : IRequest<AuthenticateUserResponse>
{
}

public partial class UpdateUserCommand : IRequest<UserDto>
{
}

public partial class DeleteUserCommand : IRequest
{
}

public partial class UploadUserArquivoCommand : IRequest<UserArquivoDto>
{
}

public partial class DeleteUserArquivoCommand : IRequest
{
}

public partial class ChangePasswordCommand : IRequest<ChangePasswordResponse>
{
}

public partial class ResetUserPasswordCommand : IRequest<ResetUserPasswordResponse>
{
}

public partial class ResetUserPasswordByEmailCommand : IRequest<RequestPasswordResetResponse>
{
}

public partial class ConfirmPasswordResetCommand : IRequest<ResetUserPasswordResponse>
{
}
