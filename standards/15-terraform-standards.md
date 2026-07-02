# 🏗️ Padrões de Codificação Terraform

Este documento define os padrões obrigatórios de codificação Terraform para todos os módulos e ambientes da infraestrutura CorePoints. O Kiro AI deve validar e rejeitar qualquer código que viole estas diretrizes.

---

## 1. 🏷️ Estratégia de Tags (Padrão Corporativo)

### Regra Central
Tags obrigatórias (`Environment`, `Project`, `ManagedBy`) são aplicadas **exclusivamente** via `default_tags` no bloco `provider "aws"` do módulo raiz do ambiente. Os módulos filhos **nunca** devem repetir essas tags.

### No módulo raiz do ambiente (`environments/{env}/main.tf`)
```hcl
provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Environment = var.environment       # ex: "dev", "prod"
      Project     = "CorePoints"          # Nome fixo do projeto
      ManagedBy   = "Terraform"           # Sempre "Terraform"
    }
  }
}
```

### Nos módulos filhos (network, compute, database, security)
Cada recurso deve declarar **apenas** a tag `Name`. As demais são herdadas automaticamente.

```hcl
# ✅ CORRETO
resource "aws_vpc" "main" {
  cidr_block = var.vpc_cidr

  tags = {
    Name = "${var.environment}-vpc"
  }
}

# ❌ ERRADO — duplicação das tags obrigatórias
resource "aws_vpc" "main" {
  cidr_block = var.vpc_cidr

  tags = {
    Name        = "${var.environment}-vpc"
    Environment = var.environment   # ← remover
    Project     = "CorePoints"      # ← remover
    ManagedBy   = "Terraform"       # ← remover
  }
}
```

### Tags obrigatórias por recurso

| Tag | Onde definir | Valor |
|-----|-------------|-------|
| `Environment` | `default_tags` (provider) | `var.environment` |
| `Project` | `default_tags` (provider) | `"CorePoints"` |
| `ManagedBy` | `default_tags` (provider) | `"Terraform"` |
| `Name` | Bloco `tags` de cada recurso | `"${var.environment}-<nome-descritivo>"` |

---

## 2. 🔐 IAM — Princípio do Menor Privilégio

### Regra Central
Políticas IAM **nunca** devem usar `Resource = ["*"]`. Toda política deve referenciar os ARNs ou paths específicos dos recursos que serão acessados.

### Padrão para acesso ao SSM Parameter Store

O escopo mínimo aceito é o path completo do ambiente. ARN exato por parâmetro é preferível quando o número de segredos for pequeno.

```hcl
# ✅ CORRETO — restrito ao path do ambiente
resource "aws_iam_role_policy" "ecs_ssm_read" {
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AllowSSMRead"
        Effect = "Allow"
        Action = ["ssm:GetParameters", "ssm:GetParameter"]
        Resource = [
          "arn:aws:ssm:${var.aws_region}:*:parameter${var.ssm_parameter_path}"
          # var.ssm_parameter_path = "/${var.environment}/*"
        ]
      },
      {
        Sid    = "AllowKMSDecrypt"
        Effect = "Allow"
        Action = ["kms:Decrypt"]
        Resource = [var.kms_key_arn]  # ARN específico da chave KMS
      }
    ]
  })
}

# ❌ ERRADO — escopo aberto para toda a conta AWS
resource "aws_iam_role_policy" "ecs_ssm_read" {
  policy = jsonencode({
    Statement = [{
      Action   = ["ssm:GetParameters", "kms:Decrypt"]
      Resource = ["*"]   # ← nunca usar
    }]
  })
}
```

### Passagem de ARNs entre módulos

Quando um módulo filho precisa de ARNs de recursos criados em outro módulo, eles devem ser passados como variáveis via `outputs` + `variables`, nunca hardcoded.

```hcl
# security/outputs.tf — expõe o ARN
output "kms_key_arn" {
  value = aws_kms_key.security_key.arn
}

# environments/dev/main.tf — passa o ARN como variável
module "compute" {
  kms_key_arn        = module.security.kms_key_arn
  ssm_parameter_path = "/${var.environment}/*"
}

# compute/variables.tf — declara a variável
variable "kms_key_arn" {
  type        = string
  description = "ARN da chave KMS para restringir a política IAM do ECS"
}

variable "ssm_parameter_path" {
  type        = string
  description = "Path do SSM Parameter Store acessível pelo ECS (ex: /dev/*)"
}
```

---

## 3. 🔒 Criptografia em Repouso (Mensageria)

### Regra Central
Todo recurso que armazena dados deve ter criptografia em repouso ativada. Isso inclui SQS, SNS, RDS, ElastiCache, volumes EBS e parâmetros SSM.

### Padrão para SQS e SNS (ambiente DEV)

Use as chaves gerenciadas pela AWS (`alias/aws/sqs` e `alias/aws/sns`). São gratuitas e não exigem gerenciamento de rotação.

```hcl
# ✅ SNS com criptografia
resource "aws_sns_topic" "events" {
  name              = "${var.environment}-events-topic"
  kms_master_key_id = "alias/aws/sns"

  tags = { Name = "${var.environment}-events-topic" }
}

# ✅ SQS com criptografia
resource "aws_sqs_queue" "main" {
  name              = "${var.environment}-events-queue"
  kms_master_key_id = "alias/aws/sqs"

  tags = { Name = "${var.environment}-events-queue" }
}

# ❌ ERRADO — sem criptografia
resource "aws_sqs_queue" "main" {
  name = "${var.environment}-events-queue"
  # kms_master_key_id ausente → dados em texto claro em repouso
}
```

### Tabela de referência de criptografia por recurso

| Recurso | Atributo | Valor (DEV) |
|---------|----------|-------------|
| `aws_sqs_queue` | `kms_master_key_id` | `"alias/aws/sqs"` |
| `aws_sns_topic` | `kms_master_key_id` | `"alias/aws/sns"` |
| `aws_db_instance` | `storage_encrypted` | `true` |
| `aws_elasticache_replication_group` | `at_rest_encryption_enabled` | `true` |
| `aws_ssm_parameter` | `type = "SecureString"` + `key_id` | ARN da chave KMS |
| `aws_kms_key` | `enable_key_rotation` | `true` |

---

## 4. 📐 Estrutura de Módulos

### Convenção de arquivos (obrigatória para todo módulo)

```
modules/
  {nome-do-modulo}/
    main.tf       # Recursos principais
    variables.tf  # Inputs do módulo (com description em todos)
    outputs.tf    # Outputs exportados (com description em todos)
```

### Variáveis sem `description` são proibidas

```hcl
# ✅ CORRETO
variable "environment" {
  type        = string
  description = "Nome do ambiente (dev, prod, etc)"
}

# ❌ ERRADO
variable "environment" {
  type = string
}
```

### Isolamento de parâmetros por ambiente

A lógica dos módulos centrais **nunca** deve conter valores específicos de ambiente (nomes de buckets, CIDRs, tamanhos de instância). Tudo deve vir de `variables.tf` e ser alimentado pelo `terraform.tfvars` do ambiente.

---

## 5. 🏷️ Naming Convention de Recursos AWS

O formato padrão para a tag `Name` é `{env}-{servico}-{tipo}`. A tabela abaixo define os nomes canônicos por tipo de recurso para garantir consistência entre módulos e facilitar a localização no Console AWS.

### Rede (`network`)

| Recurso Terraform | Padrão de Nome |
|---|---|
| `aws_vpc` | `{env}-vpc` |
| `aws_internet_gateway` | `{env}-igw` |
| `aws_subnet` (pública) | `{env}-public-subnet-{az}` (ex: `dev-public-subnet-a`) |
| `aws_subnet` (privada) | `{env}-private-subnet-{az}` (ex: `dev-private-subnet-a`) |
| `aws_route_table` (público) | `{env}-public-rt` |
| `aws_route_table` (privado) | `{env}-private-rt` |

### Segurança (`security`)

| Recurso Terraform | Padrão de Nome |
|---|---|
| `aws_kms_key` | `{env}-security-kms-key` |
| `aws_ssm_parameter` (senha db) | `/{env}/database/admin_password` (path, não tag Name) |
| `aws_cognito_user_pool` | `{env}-user-pool` |

### Computação e Borda (`compute`)

| Recurso Terraform | Padrão de Nome |
|---|---|
| `aws_ecs_cluster` | `{env}-ecs-cluster` |
| `aws_ecs_service` | `{env}-{servico}-service` (ex: `dev-app-service`) |
| `aws_ecs_task_definition` | `{env}-{servico}-task` |
| `aws_security_group` (ECS) | `{env}-ecs-app-sg` |
| `aws_apigatewayv2_api` | `{env}-api-gateway` |
| `aws_apigatewayv2_vpc_link` | `{env}-vpc-link` |
| `aws_iam_role` (execução ECS) | `{env}-ecs-execution-role` |
| `aws_iam_role` (task ECS) | `{env}-ecs-task-role` |
| `aws_cloudwatch_log_group` | `/ecs/{env}-{servico}` |
| `aws_sns_topic` | `{env}-{dominio}-topic` (ex: `dev-events-topic`) |
| `aws_sqs_queue` | `{env}-{dominio}-queue` (ex: `dev-events-queue`) |
| `aws_sqs_queue` (DLQ) | `{env}-{dominio}-dlq` (ex: `dev-events-dlq`) |

### Banco de Dados e Cache (`database`)

| Recurso Terraform | Padrão de Nome |
|---|---|
| `aws_db_instance` | `{env}-{servico}-db` (ex: `dev-microsservico-db`) |
| `aws_db_subnet_group` | `{env}-rds-subnet-group` |
| `aws_security_group` (RDS) | `{env}-rds-postgres-sg` |
| `aws_elasticache_replication_group` | `{env}-{servico}-cache` (ex: `dev-cache-redis`) |
| `aws_elasticache_subnet_group` | `{env}-redis-subnet-group` |
| `aws_security_group` (Redis) | `{env}-redis-sg` |

---

## 6. 🚫 Checklist de Revisão

Antes de abrir um PR com código Terraform, valide:

- [ ] Nenhum recurso com `Resource = ["*"]` em políticas IAM
- [ ] Nenhum bloco `tags` com `Environment`, `Project` ou `ManagedBy` nos módulos filhos
- [ ] `Project = "CorePoints"` definido no `default_tags` do provider
- [ ] Toda fila SQS com `kms_master_key_id`
- [ ] Todo tópico SNS com `kms_master_key_id`
- [ ] Todo banco RDS com `storage_encrypted = true` e `skip_final_snapshot = true` (em DEV)
- [ ] Todo Redis com `at_rest_encryption_enabled = true`
- [ ] Toda variável com `description` preenchida
- [ ] ARNs de outros módulos passados via `outputs` + `variables`, nunca hardcoded
- [ ] Tags `Name` no formato `"${var.environment}-<nome-descritivo>"`
