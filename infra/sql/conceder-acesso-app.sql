-- Dá à identidade gerenciada da Web App o mínimo de que a aplicação precisa: ler e gravar
-- dados. Migrations e o reset da demo rodam como a identidade do GitHub Actions, que
-- administra o SQL. Rodado pelo workflow de deploy a cada versão; idempotente.
--
-- O usuário nasce pelo clientId (WITH SID ... TYPE = E), sem consultar o diretório do
-- Entra: a identidade do GitHub Actions não tem, nem precisa ter, permissão para isso.
--
-- Variáveis (sqlcmd -v): AppIdentityName e AppIdentityClientId.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppIdentityName)')
BEGIN
    DECLARE @sid nvarchar(64) = CONVERT(nvarchar(64),
        CONVERT(varbinary(16), CAST(N'$(AppIdentityClientId)' AS uniqueidentifier)), 1);
    EXEC (N'CREATE USER [$(AppIdentityName)] WITH SID = ' + @sid + N', TYPE = E;');
END;

ALTER ROLE db_datareader ADD MEMBER [$(AppIdentityName)];
ALTER ROLE db_datawriter ADD MEMBER [$(AppIdentityName)];
