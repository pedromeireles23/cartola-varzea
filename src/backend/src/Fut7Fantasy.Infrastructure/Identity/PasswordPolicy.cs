using Microsoft.AspNetCore.Identity;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Recusa senha que contenha a parte local do e-mail.
///
/// A politica de senha do projeto e comprimento minimo sem regras de composicao
/// (04-seguranca §5): exigir simbolo e maiuscula empurra a pessoa para senhas
/// curtas e previsiveis. Esta validacao cobre o caso obvio que o comprimento
/// sozinho nao pega.
/// </summary>
public sealed class PasswordPolicy : IPasswordValidator<ApplicationUser>
{
    private const int MinimoDaParteLocal = 4;

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var parteLocal = email.Split('@', 2)[0];
        if (parteLocal.Length >= MinimoDaParteLocal
            && password.Contains(parteLocal, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordContainsEmail",
                Description = "A senha não pode conter o seu e-mail.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
