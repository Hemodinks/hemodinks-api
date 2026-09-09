using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class ResetUserPasswordCommandHandler(TemporaryAccessService service)
    : IRequestHandler<ResetUserPasswordCommand, ResetUserPasswordResponse>
{
    public Task<ResetUserPasswordResponse> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
        => service.GenerateAsync(request.UserId, request.CurrentUser, cancellationToken);
}
