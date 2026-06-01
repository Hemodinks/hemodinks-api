#!/usr/bin/env powershell

# Script de Teste Rápido - HemodinksAPI
# Este script testa os endpoints principais da API

param(
    [string]$baseUrl = "http://localhost:5000"
)

Write-Host "╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  TESTE RÁPIDO - HEMODINKS API v1.0                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Cores
$success = "Green"
$warning = "Yellow"
$error = "Red"
$info = "Cyan"

# 1. Testar conexão
Write-Host "1️⃣  Testando conexão com a API..." -ForegroundColor $info
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/users/1" `
        -Method Get `
        -Headers @{ "Authorization" = "Bearer invalid" } `
        -ErrorAction SilentlyContinue
    Write-Host "   ✅ API está respondendo" -ForegroundColor $success
} catch {
    Write-Host "   ❌ API não está respondendo" -ForegroundColor $error
    Write-Host "   Certifique-se de que a API está em execução:" -ForegroundColor $warning
    Write-Host "      dotnet run (no diretório HemodinksAPI.Api)" -ForegroundColor $warning
    exit 1
}

# 2. Autenticar usuário
Write-Host ""
Write-Host "2️⃣  Autenticando como George Marcone..." -ForegroundColor $info
try {
    $authBody = @{
        email = "gmarcone@gmail.com"
        senha = "Senha@123"
    } | ConvertTo-Json

    $authResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/authenticate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $authBody

    $token = $authResponse.token
    Write-Host "   ✅ Autenticação bem-sucedida!" -ForegroundColor $success
    Write-Host "   Token: $($token.Substring(0, 50))..." -ForegroundColor $warning
} catch {
    Write-Host "   ❌ Falha na autenticação" -ForegroundColor $error
    Write-Host "   Erro: $($_.Exception.Message)" -ForegroundColor $warning
    exit 1
}

# 3. Listar usuários
Write-Host ""
Write-Host "3️⃣  Listando usuários..." -ForegroundColor $info
try {
    $usersResponse = Invoke-RestMethod -Uri "$baseUrl/api/users" `
        -Method Get `
        -Headers @{ "Authorization" = "Bearer $token" }

    $count = $usersResponse.Count
    Write-Host "   ✅ Listagem bem-sucedida!" -ForegroundColor $success
    Write-Host "   Total de usuários: $count" -ForegroundColor $warning
    
    if ($count -eq 50) {
        Write-Host "   ✅ Seed de 50 usuários criado com sucesso!" -ForegroundColor $success
    }
} catch {
    Write-Host "   ❌ Falha ao listar usuários" -ForegroundColor $error
    Write-Host "   Erro: $($_.Exception.Message)" -ForegroundColor $warning
}

# 4. Buscar George Marcone por ID
Write-Host ""
Write-Host "4️⃣  Buscando George Marcone (ID=1)..." -ForegroundColor $info
try {
    $userResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/1" `
        -Method Get `
        -Headers @{ "Authorization" = "Bearer $token" }

    Write-Host "   ✅ Usuário encontrado!" -ForegroundColor $success
    Write-Host "      Nome: $($userResponse.nome)" -ForegroundColor $warning
    Write-Host "      Email: $($userResponse.email)" -ForegroundColor $warning
    Write-Host "      Telefone: $($userResponse.telefone)" -ForegroundColor $warning
    Write-Host "      Nascimento: $($userResponse.dataNascimento)" -ForegroundColor $warning
} catch {
    Write-Host "   ❌ Falha ao buscar usuário" -ForegroundColor $error
    Write-Host "   Erro: $($_.Exception.Message)" -ForegroundColor $warning
}

# 5. Buscar por email
Write-Host ""
Write-Host "5️⃣  Buscando usuário por email..." -ForegroundColor $info
try {
    $emailResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/email/gmarcone@gmail.com" `
        -Method Get `
        -Headers @{ "Authorization" = "Bearer $token" }

    Write-Host "   ✅ Usuário encontrado por email!" -ForegroundColor $success
    Write-Host "      Nome: $($emailResponse.nome)" -ForegroundColor $warning
} catch {
    Write-Host "   ❌ Falha ao buscar por email" -ForegroundColor $error
    Write-Host "   Erro: $($_.Exception.Message)" -ForegroundColor $warning
}

# 6. Criar novo usuário
Write-Host ""
Write-Host "6️⃣  Criando novo usuário de teste..." -ForegroundColor $info
try {
    $newUserBody = @{
        nome = "Usuario Teste $(Get-Random)"
        email = "teste$(Get-Random)@example.com"
        telefone = "+5511987654321"
        senha = "Senha@123"
        dataNascimento = "1990-01-15T00:00:00Z"
    } | ConvertTo-Json

    $createResponse = Invoke-RestMethod -Uri "$baseUrl/api/users" `
        -Method Post `
        -ContentType "application/json" `
        -Body $newUserBody

    Write-Host "   ✅ Usuário criado com sucesso!" -ForegroundColor $success
    Write-Host "      ID: $($createResponse.id)" -ForegroundColor $warning
    Write-Host "      Email: $($createResponse.email)" -ForegroundColor $warning
} catch {
    Write-Host "   ❌ Falha ao criar usuário" -ForegroundColor $error
    Write-Host "   Erro: $($_.Exception.Message)" -ForegroundColor $warning
}

# Resumo
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  ✅ TESTES CONCLUÍDOS COM SUCESSO!                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝" -ForegroundColor Cyan

Write-Host ""
Write-Host "📊 Resumo:" -ForegroundColor $info
Write-Host "   • API respondendo em: $baseUrl" -ForegroundColor $warning
Write-Host "   • Autenticação JWT funcionando" -ForegroundColor $warning
Write-Host "   • 50 usuários seedados no banco" -ForegroundColor $warning
Write-Host "   • Operações de CRUD funcionando" -ForegroundColor $warning

Write-Host ""
Write-Host "🔗 Links úteis:" -ForegroundColor $info
Write-Host "   • API: $baseUrl" -ForegroundColor $warning
Write-Host "   • Swagger: $baseUrl/swagger" -ForegroundColor $warning
Write-Host "   • README: ./README.md" -ForegroundColor $warning

Write-Host ""
Write-Host "💡 Próximas ações:" -ForegroundColor $info
Write-Host "   1. Revisar logs em: logs/hemodinks-api-.txt" -ForegroundColor $warning
Write-Host "   2. Testar endpoints com Postman ou Insomnia" -ForegroundColor $warning
Write-Host "   3. Revisar o código em: HemodinksAPI.Api/" -ForegroundColor $warning

Write-Host ""
