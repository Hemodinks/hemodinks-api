# 🚀 Guia de Primeira Execução - HemodinksAPI

## ⏱️ Tempo estimado: 10-15 minutos

---

## 📋 Pré-requisitos

### Opção 1: Executar com Docker (Recomendado)
- ✅ Docker instalado ([Download](https://www.docker.com/products/docker-desktop))
- ✅ Docker Compose instalado (incluído com Docker Desktop)
- ✅ ~2GB de espaço em disco

### Opção 2: Desenvolvimento Local
- ✅ .NET 10 SDK instalado ([Download](https://dotnet.microsoft.com/download/dotnet/10.0))
- ✅ SQL Server instalado (Local, Express ou Azure)
- ✅ PowerShell 5.0+ ou Terminal

---

## 🐳 Passo 1: Executar com Docker (Recomendado)

### 1.1 Abrir PowerShell ou Terminal

```bash
cd "c:\George Marcone\GitHub\personal\HEMODINKS\hemodinks-api"
```

### 1.2 Iniciar os containers

```bash
docker-compose up -d
```

**Aguarde 30-60 segundos** até que o SQL Server inicie completamente.

### 1.3 Verificar status

```bash
docker-compose ps
```

Deve mostrar:
```
NAME                 STATUS              PORTS
hemodinks-api        Up (healthy)        0.0.0.0:5000->5000/tcp
hemodinks-mssql      Up (healthy)        0.0.0.0:1433->1433/tcp
```

### 1.4 Acessar a API

```
http://localhost:5000/swagger
```

✅ **Pronto! A API está rodando!**

---

## 💻 Passo 2: Executar Desenvolvimento Local (Alternativo)

### 2.1 Abrir PowerShell e entrar no diretório

```bash
cd "c:\George Marcone\GitHub\personal\HEMODINKS\hemodinks-api\HemodinksAPI.Api"
```

### 2.2 Restaurar dependências

```bash
dotnet restore
```

### 2.3 Aplicar migrations

```bash
dotnet ef database update
```

### 2.4 Executar a aplicação

```bash
dotnet run
```

Deve aparecer:
```
info: Microsoft.EntityFrameworkCore.Infrastructure
      Entity Framework Core 10.0.0 initialized 'AppDbContext'
info: HemodinksAPI.Api.Seeders.UserSeeder
      Iniciando seed de dados
info: HemodinksAPI.Api.Seeders.UserSeeder
      Seed de 50 usuários concluído com sucesso
```

✅ **API está rodando em:** `https://localhost:7000` ou `http://localhost:5000`

---

## 🧪 Passo 3: Testar a API

### Opção A: Script PowerShell (Recomendado)

```bash
.\test-api.ps1
```

Ele testará automaticamente todos os endpoints principais.

### Opção B: Curl (Manual)

#### 1. Autenticar

```bash
curl -X POST http://localhost:5000/api/users/authenticate `
  -H "Content-Type: application/json" `
  -d '{
    "email": "gmarcone@gmail.com",
    "senha": "Senha@123"
  }'
```

**Resposta esperada:**
```json
{
  "id": 1,
  "nome": "George Marcone Morais dos Santos",
  "email": "gmarcone@gmail.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Copie o `token` para os próximos passos.**

#### 2. Listar usuários

```bash
curl -X GET http://localhost:5000/api/users `
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Deve retornar 50 usuários.**

#### 3. Buscar George Marcone

```bash
curl -X GET http://localhost:5000/api/users/1 `
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Resposta esperada:**
```json
{
  "id": 1,
  "nome": "George Marcone Morais dos Santos",
  "email": "gmarcone@gmail.com",
  "telefone": "+5581997236704",
  "dataCadastro": "2026-06-01T10:00:00Z",
  "dataNascimento": "1982-02-25T00:00:00Z",
  "ativo": true
}
```

### Opção C: Postman/Insomnia

Importar arquivo `API.http` na raiz do projeto ou usar as requisições manualmente:

1. **POST** `http://localhost:5000/api/users/authenticate`
   - Body JSON com email e senha
   
2. **GET** `http://localhost:5000/api/users`
   - Header: `Authorization: Bearer <token>`

---

## 📊 Passo 4: Verificar o Banco de Dados

### Com SQL Server Management Studio

1. Conectar ao servidor:
   - **Server name:** `localhost` ou `.`
   - **Authentication:** Windows ou SQL Authentication
   - **Login:** sa (se Docker) / seu usuário (se local)
   - **Password:** Hemodinks@2024! (se Docker)

2. Expandir Databases → HemodinksDB → Tables → Users

3. Clique direito em `dbo.Users` → View Top 1000 Rows

Deve mostrar **50 registros**, com George Marcone no topo.

### Com SQL Query

```sql
USE HemodinksDB
SELECT TOP 5 * FROM Users ORDER BY DataCadastro DESC
```

---

## 📝 Passo 5: Ver Logs

### Se usando Docker

```bash
docker logs -f hemodinks-api
```

### Se rodando localmente

```bash
Get-Content logs/hemodinks-api-*.txt -Wait
```

---

## ✅ Checklist de Verificação

- [ ] Docker ou .NET 10 instalado
- [ ] Arquivo `docker-compose.yml` existe
- [ ] Containers rodando (`docker ps`)
- [ ] API respondendo (`http://localhost:5000`)
- [ ] Autenticação funcionando
- [ ] 50 usuários no banco
- [ ] George Marcone encontrado (ID=1)
- [ ] Logs aparecem

---

## 🎯 Próximos Passos

### 1. Explorar a API
```bash
# Ver documentação interativa
http://localhost:5000/swagger
```

### 2. Criar novo usuário

```bash
curl -X POST http://localhost:5000/api/users `
  -H "Content-Type: application/json" `
  -d '{
    "nome": "Seu Nome",
    "email": "seu.email@example.com",
    "telefone": "+5511987654321",
    "senha": "Senha@123",
    "dataNascimento": "1990-01-15T00:00:00Z"
  }'
```

### 3. Revisar o código

Estrutura recomendada de revisão:
1. `Program.cs` - Configuração principal
2. `Models/User.cs` - Entidade
3. `Data/AppDbContext.cs` - EF Core
4. `Features/Users/Commands/` - CQRS Commands
5. `Features/Users/Queries/` - CQRS Queries
6. `Authentication/` - JWT

### 4. Modificar configurações

Editar `appsettings.json`:
```json
{
  "JwtSettings": {
    "ExpirationMinutes": 120,  // Aumentar expiração
    "SecretKey": "sua_chave_super_segura"  // Alterar chave
  },
  "ConnectionStrings": {
    "DefaultConnection": "seu_connection_string"  // Outro banco
  }
}
```

---

## 🆘 Problemas na Primeira Execução?

### Docker não funciona
```bash
# Verificar Docker
docker --version
docker run hello-world

# Se erro, reinstalar Docker Desktop
```

### Porta 5000 em uso
```bash
# Encontrar processo
Get-Process | Where-Object { $_.ProcessName -eq "dotnet" } | Stop-Process -Force

# Ou mudar porta em docker-compose.yml
```

### SQL Server não inicia
```bash
# Logs do SQL Server
docker logs hemodinks-mssql

# Aguardar 60 segundos e tentar novamente
Start-Sleep -Seconds 60
docker-compose ps
```

### Migrations não aplicam
```bash
# Resetar banco
dotnet ef database drop -f
dotnet ef database update
```

Consulte [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) para mais soluções.

---

## 📚 Documentação

| Arquivo | Conteúdo |
|---------|----------|
| [README.md](./README.md) | Documentação geral da API |
| [IMPLEMENTACAO.md](./IMPLEMENTACAO.md) | Detalhes de implementação |
| [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) | Soluções de problemas |
| [API.http](./API.http) | Exemplos de requisições |

---

## 🎓 Informações Importantes

### Dados do George Marcone (ID=1)
- **Nome:** George Marcone Morais dos Santos
- **Email:** gmarcone@gmail.com
- **Telefone:** +5581997236704
- **Nascimento:** 25/02/1982
- **Senha padrão:** Senha@123

### Todos os 50 usuários
- Senha padrão: `Senha@123`
- Status: Ativo
- Email: Único (pode-se usar para teste de duplicação)

### Configuração JWT
- **Expiração:** 60 minutos
- **Algoritmo:** HS256 (HMAC SHA256)
- **Issuer:** HemodinksAPI
- **Audience:** HemodinksAPI

---

## 🎉 Parabéns!

Sua API HemodinksAPI está **100% funcional** e pronta para uso!

### O que foi entregue:

✅ API REST completa com CQRS  
✅ Autenticação JWT implementada  
✅ 50 usuários seedados (incluindo George Marcone)  
✅ Logging estruturado com Serilog  
✅ Docker e Docker Compose configurados  
✅ Documentação completa  
✅ Scripts de teste  

### Contato

**Desenvolvedor:** George Marcone Morais dos Santos  
**Email:** gmarcone@gmail.com  
**Data:** Junho 2026

---

**Boa exploração! 🚀**
