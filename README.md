# Hemodinks API

API robusta desenvolvida em **.NET 10** com arquitetura **CQRS**, autenticação **JWT**, logging com **Serilog**, persistência com **Entity Framework Core**, e containerização com **Docker**.

## 📋 Características

- ✅ Arquitetura **CQRS** (Command Query Responsibility Segregation)
- ✅ Autenticação e Autorização com **JWT** (JSON Web Tokens)
- ✅ Logging estruturado com **Serilog**
- ✅ Persistência de dados com **Entity Framework Core 10**
- ✅ Banco de dados **SQL Server**
- ✅ Containerização com **Docker** e **Docker Compose**
- ✅ **50 usuários seedados** incluindo o usuário: George Marcone Morais dos Santos
- ✅ API RESTful com **Swagger/OpenAPI**

## 🗄️ Estrutura do Banco de Dados

### Tabela: Users

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | int | Identificador único (PK) |
| Nome | string(255) | Nome completo |
| Email | string(255) | Email (Unique) |
| Telefone | string(20) | Telefone com código de país |
| Senha | string(500) | Senha com hash PBKDF2 |
| DataCadastro | datetime | Data de cadastro |
| DataNascimento | datetime | Data de nascimento |
| Ativo | bool | Indica se ativo (default: true) |

## 🚀 Como Executar

### Opção 1: Com Docker Compose (Recomendado)

```bash
# Ir para o diretório do projeto
cd c:\George Marcone\GitHub\personal\HEMODINKS\hemodinks-api

# Criar o arquivo local de variaveis, que nao deve ser versionado
Copy-Item .env.example .env

# Ajustar MSSQL_SA_PASSWORD e JWT_SECRET_KEY no arquivo .env

# Executar com Docker Compose
docker-compose up -d
```

A API estará disponível em: `http://localhost:5000`

**Credenciais do SQL Server:**
- Usuário: `sa`
- Senha: configurada em `.env`

As credenciais do SQL Server e a chave JWT ficam fora do repositorio.

### Opção 2: Desenvolvimento Local

**Pré-requisitos:**
- .NET 10 SDK instalado
- SQL Server instalado e em execução

**Passos:**

```bash
# Entrar no diretório da API
cd HemodinksAPI.Api

# Restaurar dependências
dotnet restore

# Configurar secrets locais
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"

# Executar migrações
dotnet ef database update

# Executar a aplicação
dotnet run
```

A API estará disponível em: `https://localhost:7000` ou `http://localhost:5000`

## 📖 Endpoints da API

### Autenticação (Sem autenticação JWT)

#### POST `/api/users/authenticate`
Autentica um usuário e retorna um token JWT.

**Request:**
```json
{
  "email": "gmarcone@gmail.com",
  "senha": "Senha@123"
}
```

**Response:**
```json
{
  "id": 1,
  "nome": "George Marcone Morais dos Santos",
  "email": "gmarcone@gmail.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

#### POST `/api/users`
Cria um novo usuário.

**Request:**
```json
{
  "nome": "João Silva",
  "email": "joao@example.com",
  "telefone": "+5511987654321",
  "senha": "Senha@123",
  "dataNascimento": "1990-05-15"
}
```

**Response:**
```json
{
  "id": 51,
  "nome": "João Silva",
  "email": "joao@example.com",
  "telefone": "+5511987654321",
  "dataCadastro": "2026-06-01T10:30:00Z",
  "dataNascimento": "1990-05-15",
  "ativo": true
}
```

### Usuários (Requer autenticação JWT)

#### GET `/api/users`
Lista todos os usuários.

**Headers:**
```
Authorization: Bearer <token_jwt>
```

**Response:**
```json
[
  {
    "id": 1,
    "nome": "George Marcone Morais dos Santos",
    "email": "gmarcone@gmail.com",
    "telefone": "+5581997236704",
    "dataCadastro": "2026-06-01T10:00:00Z",
    "dataNascimento": "1982-02-25",
    "ativo": true
  },
  ...
]
```

#### GET `/api/users/{id}`
Busca um usuário por ID.

**Headers:**
```
Authorization: Bearer <token_jwt>
```

**Response:**
```json
{
  "id": 1,
  "nome": "George Marcone Morais dos Santos",
  "email": "gmarcone@gmail.com",
  "telefone": "+5581997236704",
  "dataCadastro": "2026-06-01T10:00:00Z",
  "dataNascimento": "1982-02-25",
  "ativo": true
}
```

#### GET `/api/users/email/{email}`
Busca um usuário por email.

**Headers:**
```
Authorization: Bearer <token_jwt>
```

## 🔐 Segurança

### Hash de Senha
- Algoritmo: **PBKDF2** (RFC 2898)
- Hash Function: **SHA256**
- Iterações: **10000**
- Salt: **16 bytes**

### JWT Token
- Algoritmo: **HS256**
- Expiração: **60 minutos** (configurável)
- Issuer: `HemodinksAPI`
- Audience: `HemodinksAPI`

## 📊 Logging

Os logs são armazenados em:
- **Console:** Output em tempo real
- **Arquivo:** `logs/hemodinks-api-.txt` (rotação diária)

**Formato do Log:**
```
2026-06-01 10:30:45.123 +00:00 [INF] Criando novo usuário: joao@example.com
```

## 🔧 Configuração

### Variaveis de ambiente e User Secrets

`appsettings.json` nao armazena segredos. Configure os valores por variaveis de ambiente, `.env` no Docker ou User Secrets no desenvolvimento local.

```bash
ConnectionStrings__DefaultConnection="Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
JwtSettings__SecretKey="troque_por_uma_chave_com_32_caracteres_ou_mais"
JwtSettings__Issuer="HemodinksAPI"
JwtSettings__Audience="HemodinksAPI"
JwtSettings__ExpirationMinutes="60"
```

## 🛠️ Estrutura do Projeto

```
HemodinksAPI.Api/
├── Models/                    # Entidades de domínio
│   └── User.cs
├── Data/                      # Contexto EF Core
│   ├── AppDbContext.cs
│   └── Migrations/
├── Features/Users/            # CQRS
│   ├── Commands/
│   │   ├── UserCommands.cs
│   │   └── UserCommandHandlers.cs
│   └── Queries/
│       ├── UserQueries.cs
│       └── UserQueryHandlers.cs
├── Authentication/            # JWT
│   ├── JwtSettings.cs
│   └── JwtTokenService.cs
├── Utils/                     # Utilitários
│   └── PasswordHasher.cs
├── Seeders/                   # Seed de dados
│   └── UserSeeder.cs
├── Program.cs                 # Configuração principal
├── appsettings.json           # Configurações
└── Dockerfile

```

## 📝 Dados Seedados

A aplicação cria automaticamente 50 usuários na primeira execução, incluindo:

**Usuário Especial:**
- Nome: George Marcone Morais dos Santos
- Email: gmarcone@gmail.com
- Telefone: +5581997236704
- Nascimento: 25/02/1982
- Senha: Senha@123

Todos os usuários têm a mesma senha padrão: `Senha@123`

## 🐳 Docker

### Build da Imagem
```bash
docker build -t hemodinks-api:latest .
```

### Executar Container
```bash
docker run -p 5000:5000 \
  -e ConnectionStrings__DefaultConnection="Server=seu_servidor;Database=HemodinksDB;..." \
  hemodinks-api:latest
```

## 📚 Tecnologias

- **.NET 10**: Framework base
- **ASP.NET Core**: Web Framework
- **Entity Framework Core 10**: ORM
- **MediatR**: Implementação de CQRS
- **Serilog**: Logging estruturado
- **JWT Bearer**: Autenticação
- **Swashbuckle**: Swagger/OpenAPI
- **SQL Server**: Banco de dados
- **Docker**: Containerização

## 🤝 Contribuição

Para contribuir, faça um fork do projeto e envie um pull request.

## 📄 Licença

Este projeto está sob a licença MIT.

## ✉️ Suporte

Para suporte, entre em contato com: gmarcone@gmail.com

---

**Desenvolvido em .NET 10 com ❤️**
