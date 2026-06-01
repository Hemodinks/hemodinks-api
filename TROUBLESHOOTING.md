# 🔧 Guia de Troubleshooting - HemodinksAPI

## Problemas Comuns e Soluções

---

## ❌ Erro: "API não está respondendo"

### Causa
A API não está em execução ou não está acessível no endereço configurado.

### Solução
```bash
# Verificar se a API está rodando (desenvolvimento local)
cd HemodinksAPI.Api
dotnet run

# Com Docker Compose
docker-compose up -d

# Verificar se está rodando
docker ps | findstr hemodinks
```

---

## ❌ Erro: "Falha ao conectar ao banco de dados"

### Mensagem Típica
```
An error occurred using the connection to database 'HemodinksDB'
Connection timeout expired
```

### Causas Possíveis
1. SQL Server não está em execução
2. String de conexão incorreta
3. Credenciais inválidas

### Soluções

#### Se usando SQL Server local
```bash
# Verificar status do SQL Server
net start MSSQL$SQLEXPRESS

# Ou via Services (Windows)
# Win + R → services.msc → procurar "SQL Server"
```

#### Se usando Docker
```bash
# Verificar se o container SQL Server está rodando
docker ps | findstr mssql

# Logs do SQL Server
docker logs hemodinks-mssql

# Se não estiver rodando:
docker-compose up -d sqlserver
```

#### Verificar string de conexão
Arquivo: `HemodinksAPI.Api/appsettings.json`
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
}
```

---

## ❌ Erro: "Não consegue aplicar migrations"

### Mensagem Típica
```
Pending migrations detected
Unable to connect to the database
```

### Solução

```bash
cd HemodinksAPI.Api

# Ver migrations
dotnet ef migrations list

# Remover última migration (se necessário)
dotnet ef migrations remove

# Criar nova migration
dotnet ef migrations add InitialCreate --output-dir "Data/Migrations"

# Aplicar migrations
dotnet ef database update

# Resetar banco completamente
dotnet ef database drop
dotnet ef database update
```

---

## ❌ Erro: "Porta já está em uso"

### Mensagem Típica
```
System.IO.IOException: Failed to bind to address http://[::]:5000
An attempt was made to access a socket in a way forbidden by its access permissions
```

### Solução

#### Para desenvolvimento local
```bash
# Matar processo usando a porta 5000
Get-Process | Where-Object { $_.ProcessName -eq "dotnet" } | Stop-Process -Force

# Ou especificar outra porta
dotnet run --urls "http://localhost:5001"
```

#### Para Docker
```bash
# Remover container e tentar novamente
docker-compose down
docker-compose up -d

# Ou mapear para outra porta
# Editar docker-compose.yml:
# ports:
#   - "5001:5000"
```

---

## ❌ Erro: "Falha de autenticação JWT"

### Mensagem Típica
```
401 Unauthorized
JWT validation failed
```

### Causas
1. Token expirado
2. Chave secreta incorreta
3. Token formatado incorretamente
4. Header Authorization incorreto

### Solução

```bash
# Obter novo token
curl -X POST http://localhost:5000/api/users/authenticate \
  -H "Content-Type: application/json" \
  -d '{"email":"gmarcone@gmail.com","senha":"Senha@123"}'

# Usar token correto no header
# Authorization: Bearer <seu_token_aqui>

# Verificar formato do token (deve ter 3 partes separadas por pontos)
# exemplo: header.payload.signature
```

---

## ❌ Erro: "Email já cadastrado"

### Mensagem Típica
```
{
  "message": "Erro ao criar usuário",
  "error": "An error occurred in the database operation"
}
```

### Causa
Email já existe na base de dados (constraint UNIQUE).

### Solução
```bash
# Usar um email único
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Novo Usuario",
    "email": "novo_email_unico@example.com",
    "telefone": "+5511987654321",
    "senha": "Senha@123",
    "dataNascimento": "1990-01-15T00:00:00Z"
  }'
```

---

## ❌ Erro: "Logs não aparecem"

### Solução

```bash
# Verificar se a pasta logs existe
ls -Force logs/

# Ou criar manualmente
New-Item -ItemType Directory -Path "logs" -Force

# Verificar permissões
# A pasta deve ser gravável pelo usuário que executa a API

# Arquivo de log
cat logs/hemodinks-api-*.txt

# Com tail em tempo real
Get-Content logs/hemodinks-api-*.txt -Wait
```

---

## ❌ Erro: Docker Build falha

### Solução

```bash
# Limpar cache Docker
docker system prune -a --volumes

# Rebuild sem cache
docker-compose build --no-cache

# Tentar novamente
docker-compose up -d
```

---

## ❌ Performance lenta

### Causas
1. Sem índices no banco
2. N+1 queries
3. Falta de cache
4. SQL Server com pouca memória

### Verificação

```bash
# Usar SQL Server Management Studio para analisar
# Ou verificar planos de execução

# Logs de query lenta (se configurado)
cat logs/hemodinks-api-*.txt | Select-String "slow query"
```

---

## ✅ Troubleshooting Rápido

### Checklist

- [ ] API está rodando? (`dotnet run` ou `docker-compose up`)
- [ ] SQL Server está ativo? (Services Windows ou Docker)
- [ ] String de conexão está correta? (`appsettings.json`)
- [ ] Migrations foram aplicadas? (`dotnet ef database update`)
- [ ] Porta 5000 está disponível? (ou configurar outra)
- [ ] Token JWT válido? (não expirado)
- [ ] Arquivo de logs acessível? (`logs/` pasta)

---

## 📞 Debug Mode

### Ativar logging detalhado

Editar `appsettings.Development.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Debug"
    }
  },
  "Serilog": {
    "MinimumLevel": "Debug"
  }
}
```

### Executar com debug
```bash
cd HemodinksAPI.Api
dotnet run --environment Development
```

---

## 🔍 Verificações Adicionais

### Verificar saúde da API
```bash
# Testar endpoint básico
curl -v http://localhost:5000/api/users/authenticate
```

### Verificar banco de dados
```bash
# Com SQL Server Management Studio
# Server: localhost\SQLEXPRESS (ou .)
# Database: HemodinksDB
# User: sa (se local)
```

### Ver variáveis de ambiente
```bash
# Docker
docker exec hemodinks-api printenv | grep -i jwt

# Local
$env:JwtSettings__SecretKey
```

---

## 📚 Recursos Úteis

- [Documentação Entity Framework Core](https://learn.microsoft.com/ef/)
- [Documentação ASP.NET Core](https://learn.microsoft.com/aspnet/core/)
- [JWT.io - Decodificar tokens](https://jwt.io/)
- [Postman - Testar APIs](https://www.postman.com/)
- [Docker Documentation](https://docs.docker.com/)

---

## 🆘 Ainda com problemas?

1. Verificar logs: `logs/hemodinks-api-*.txt`
2. Consultar arquivo `README.md` e `IMPLEMENTACAO.md`
3. Executar script de teste: `.\test-api.ps1`
4. Contactar desenvolvedor: gmarcone@gmail.com

---

**Última atualização:** Junho 2026
