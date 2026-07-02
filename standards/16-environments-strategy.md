# 🌍 Estratégia de Ambientes (DEV → PROD)

Este documento define como a infraestrutura deve se comportar em cada ambiente e o que precisa ser decidido antes de criar o ambiente de produção. A estrutura modular do Terraform já foi projetada para suportar múltiplos ambientes sem alterar a lógica dos módulos centrais.

---

## 1. Estrutura de Pastas por Ambiente

Cada ambiente é uma pasta isolada dentro de `terraform/modules/environments/`:

```
terraform/
  modules/
    environments/
      dev/          ← Ambiente atual (Free Tier)
        main.tf
        variables.tf
        terraform.tfvars
        outputs.tf
      prod/         ← A criar quando oportuno
        main.tf
        variables.tf
        terraform.tfvars
        outputs.tf
```

Os módulos centrais (`network`, `compute`, `database`, `security`) são **compartilhados** entre os ambientes. Nunca duplique lógica de módulo para criar um novo ambiente — apenas crie uma nova pasta em `environments/` com seu próprio `terraform.tfvars`.

---

## 2. Diferenças entre Ambientes — Checklist para PROD

Quando o ambiente de produção for criado, as seguintes decisões precisam ser tomadas e documentadas no `terraform.tfvars` de prod:

| Configuração | DEV (atual) | PROD (a definir) |
|---|---|---|
| `environment` | `"dev"` | `"prod"` |
| RDS `instance_class` | `db.t4g.micro` | A definir |
| RDS `multi_az` | `false` | Recomendado: `true` |
| RDS `deletion_protection` | `false` | Recomendado: `true` |
| RDS `skip_final_snapshot` | `true` | Recomendado: `false` |
| RDS `backup_retention_period` | `0` | Recomendado: `7` (dias) |
| Redis `node_type` | `cache.t3.micro` | A definir |
| Redis `num_cache_clusters` | `1` | Recomendado: `2` (primário + réplica) |
| Fargate `cpu` | `256` (0.25 vCPU) | A definir |
| Fargate `memory` | `512` (0.5 GB) | A definir |
| Fargate `desired_count` | `1` | A definir |
| CloudWatch log retention | `7` dias | Recomendado: `30` dias |

---

## 3. O que os Módulos já Suportam

Os módulos centrais já recebem esses valores como variáveis. Não há nenhuma alteração de código necessária para criar PROD — apenas novos valores em `terraform.tfvars`. Isso é garantido pelas diretrizes de `14-terraform-standards.md` (seção de isolamento de parâmetros por ambiente).

---

## 4. State Remoto por Ambiente

Cada ambiente deve ter seu próprio **state file isolado** no S3 para evitar que um `terraform apply` de DEV interfira em PROD:

```hcl
# environments/dev/main.tf
backend "s3" {
  bucket = "corepoints-terraform-state"
  key    = "dev/terraform.tfstate"   # ← path isolado por ambiente
  region = "us-east-1"
  encrypt = true
}

# environments/prod/main.tf (quando criado)
backend "s3" {
  bucket = "corepoints-terraform-state"
  key    = "prod/terraform.tfstate"  # ← path diferente
  region = "us-east-1"
  encrypt = true
}
```

---

## 5. Regras de Proteção de PROD (a implementar antes do go-live)

Antes de criar o ambiente de produção, as seguintes proteções devem estar em vigor:

- [ ] Branch `main` com proteção: exigir PR aprovado antes de merge
- [ ] GitHub Actions com environment `production` configurado para exigir aprovação manual antes do `terraform apply`
- [ ] `deletion_protection = true` no RDS de produção
- [ ] Alertas de billing configurados no AWS Budgets para o ambiente prod
- [ ] Política de backup ativa (RDS `backup_retention_period >= 7`)
