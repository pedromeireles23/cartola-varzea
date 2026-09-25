using Fut7Fantasy.Application.Abstractions;

namespace Fut7Fantasy.Demo;

/// <summary>
/// A conta que age em cada passo do seed. Na API quem responde é a sessão HTTP; aqui o
/// seed escolhe, e os serviços continuam lendo a mesma abstração — e gravando auditoria
/// no nome de quem de fato agiria.
/// </summary>
internal sealed class DemoActor : ICurrentUser
{
    private IReadOnlyCollection<string> _roles = [];

    public Guid? Id { get; private set; }

    public bool IsAuthenticated => Id is not null;

    public bool IsInRole(string role) => _roles.Contains(role);

    public void Become(Guid userId, params string[] roles)
    {
        Id = userId;
        _roles = roles;
    }
}
