# 🐳 Diretrizes de Container Registry (Amazon ECR)

Este documento define os padrões obrigatórios para criação, versionamento e gestão de imagens Docker no Amazon ECR para o projeto CorePoints.

---

## 1. Nomenclatura de Repositórios ECR

O padrão de nome para repositórios ECR segue o mesmo princípio da convenção de recursos Terraform:

```
{projeto}/{servico}
```

Exemplos:
- `corepoints/api-financeiro`
- `corepoints/bff-web`
- `corepoints/worker-outbox`

**Regras:**
- Sempre em `kebab-case`, nunca `PascalCase` ou `snake_case`
- O prefixo `corepoints/` é obrigatório para agrupar todos os repositórios do projeto
- O nome do serviço deve refletir a responsabilidade do container, não a tecnologia (ex: `api-financeiro`, não `dotnet-app`)

---

## 2. Versionamento de Tags de Imagem

### Regra absoluta: proibido usar `:latest` em produção

A tag `:latest` não é rastreável e impede rollback controlado. Em produção, toda imagem deve ter uma tag imutável.

| Ambiente | Tag obrigatória | Exemplo |
|----------|----------------|---------|
| DEV | `{branch}-{short-sha}` | `main-a3f9b2c` |
| PROD | `{semver}` ou `{semver}-{short-sha}` | `1.2.0` ou `1.2.0-a3f9b2c` |

### Padrão de tagging no CI/CD (GitHub Actions)

```yaml
- name: Build e tag da imagem
  run: |
    IMAGE_TAG="${GITHUB_REF_NAME}-${GITHUB_SHA::7}"
    docker build -t $ECR_REGISTRY/corepoints/api-financeiro:$IMAGE_TAG .
    docker push $ECR_REGISTRY/corepoints/api-financeiro:$IMAGE_TAG
```

### Atualização da task definition do ECS

O CI/CD deve atualizar o campo `image` da task definition com a nova tag após o push. Nunca hardcode a tag no Terraform — use uma variável ou o mecanismo de deploy do ECS (`aws ecs update-service --force-new-deployment`).

---

## 3. Scan de Vulnerabilidades

- **Scan automático ao push:** Todo repositório ECR deve ter `image_scanning_configuration { scan_on_push = true }` ativado via Terraform.
- **Bloqueio por criticidade:** O pipeline de CI/CD deve falhar se o scan retornar vulnerabilidades de severidade `CRITICAL`. Vulnerabilidades `HIGH` geram alerta mas não bloqueiam o deploy em DEV (em PROD, bloquear também).
- **Frequência:** O scan contínuo (ECR Enhanced Scanning via Amazon Inspector) deve ser ativado em PROD para detectar vulnerabilidades em imagens já deployadas.

### Recurso Terraform para o repositório ECR

```hcl
resource "aws_ecr_repository" "app" {
  name                 = "corepoints/${var.service_name}"
  image_tag_mutability = "IMMUTABLE" # Garante que tags não podem ser sobrescritas

  image_scanning_configuration {
    scan_on_push = true # Scan automático a cada novo push
  }

  tags = {
    Name = "corepoints-${var.service_name}-ecr"
  }
}

# Política de ciclo de vida: mantém apenas as 10 imagens mais recentes por branch
resource "aws_ecr_lifecycle_policy" "app" {
  repository = aws_ecr_repository.app.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Manter apenas as 10 imagens mais recentes"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 10
      }
      action = { type = "expire" }
    }]
  })
}
```

---

## 4. Acesso ao ECR no CI/CD

- O pipeline do GitHub Actions deve autenticar no ECR usando a mesma role OIDC já configurada para o Terraform (ver `15-infrastructure-governance.md`, seção 3).
- A role IAM do CI/CD deve ter permissão apenas para `ecr:GetAuthorizationToken`, `ecr:BatchCheckLayerAvailability`, `ecr:PutImage`, `ecr:InitiateLayerUpload`, `ecr:UploadLayerPart`, `ecr:CompleteLayerUpload` — nunca permissão de deletar imagens ou repositórios.

---

## 5. Checklist antes de criar um novo repositório ECR

- [ ] Nome segue o padrão `corepoints/{servico}` em `kebab-case`
- [ ] `image_tag_mutability = "IMMUTABLE"` configurado
- [ ] `scan_on_push = true` configurado
- [ ] Política de ciclo de vida definida (evitar acúmulo de imagens antigas)
- [ ] Módulo Terraform criado com `variables.tf` e `outputs.tf` seguindo `14-terraform-standards.md`
