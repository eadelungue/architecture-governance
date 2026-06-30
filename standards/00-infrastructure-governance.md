# 🛡️ Diretivas de Segurança e Governança de Infraestrutura (Nuvem)

Este documento define os padrões obrigatórios de arquitetura, segurança e custos para a infraestrutura da empresa. O AWS Kiro deve validar e rejeitar qualquer código Terraform que viole estas diretivas.

---

## 1. 💰 Gestão de Custos e Alinhamento com Free Tier
* **Limitação de Recursos (DEV):** Toda máquina ou instância criada para o ambiente de desenvolvimento deve utilizar o menor tamanho elegível para o AWS Free Tier.
  * O Amazon RDS deve usar estritamente `db.t4g.micro` ou `db.t3.micro`.
  * O Amazon ElastiCache (Redis) deve usar estritamente `cache.t3.micro`.
  * O Amazon MQ (RabbitMQ) deve usar estritamente `mq.t3.micro`.
  * O AWS Fargate deve usar o tamanho mínimo de `0.25 vCPU` e `0.5 GB` de RAM.
* **Evitar Modelos Serverless Pagos por Hora:** Não utilize modelos serverless para Redis (ElastiCache Serverless) ou RabbitMQ, pois eles cobram taxas mínimas por hora fora do Free Tier. Use nós provisionados de tamanho micro.
* **Destruição Segura:** Ative `skip_final_snapshot = true` em bancos de dados de teste para evitar cobranças de armazenamento de snapshots residuais quando a infraestrutura for destruída.

---

## 2. 🌐 Isolamento de Rede (Segurança de Perímetro)
* **Arquitetura de Subnets:** A VPC deve ser dividida estritamente em Subnets Públicas e Subnets Privadas.
* **Isolamento de Dados:** É **terminantemente proibido** atribuir IPs públicos (`publicly_accessible = false`) ou colocar em subnets públicas os seguintes recursos:
  * Banco de Dados (RDS PostgreSQL)
  * Cache (ElastiCache Redis)
  * Mensageria (Amazon SQS + SNS)
  * Contêineres de Aplicação (ECS Fargate)
* **Ponto de Entrada Único:** Todo o tráfego vindo da internet deve bater obrigatoriamente e exclusivamente no **Amazon API Gateway** ou em um Load Balancer público na subnet pública.
* **Princípio do Menor Privilégio em Security Groups:** 
  * O Security Group do RDS e do Redis deve aceitar conexões de entrada (Ingress) **apenas e especificamente** vindas do Security Group do contêiner da aplicação. A porta 5432 ou 6379 nunca deve ficar aberta para o bloco `0.0.0.0/0`.

---

## 3. 🔐 Gestão de Identidades e Credenciais (Zero Trust)
* **Cofre de Senhas Obrigatório:** Nenhuma senha, chave de API, token ou credencial pode estar escrita em texto limpo (hardcoded) nos arquivos do Terraform ou nos repositórios.
* **Geração Aleatória:** As senhas master de bancos de dados devem ser geradas dinamicamente usando o recurso `random_password` do Terraform.
* **Injeção de Segredos:** As senhas geradas devem ser injetadas imediatamente no **AWS Secrets Manager** ou no **AWS Systems Manager Parameter Store (SecureString)**. O contêiner da aplicação deve ler esses dados direto do cofre via variáveis de ambiente em tempo de execução.
* **Segurança de CI/CD (Sem Chaves Fixas):** O pipeline do GitHub Actions não deve utilizar chaves de acesso fixas da AWS (`AWS_ACCESS_KEY_ID`). O pipeline deve autenticar-se na AWS via **OIDC (OpenID Connect)** usando credenciais temporárias de curta duração.

---

## 🔒 4. Criptografia e Resiliência de Dados
* **Criptografia em Repouso:** Todos os volumes de armazenamento de dados criados devem ter a criptografia ativada por padrão (`encrypted = true`), incluindo discos do RDS, volumes do RabbitMQ e chaves do Secrets Manager utilizando o AWS KMS.
* **Persistência de Contêineres:** Se qualquer banco de dados for rodado dentro de contêineres ECS, os dados devem ser mapeados em um volume do **Amazon EFS (Elastic File System)** criptografado para garantir que nenhuma transação seja perdida se o contêiner reiniciar.
* **Estratégia Multizona (Multi-AZ):** O código dos módulos deve aceitar uma lista de pelo menos duas Subnets em Zonas de Disponibilidade distintas para garantir resiliência caso um data center da AWS fique indisponível.

---

## 🤖 Instruções Específicas para o Kiro AI
Ao criar ou revisar códigos do Terraform neste repositório:
1. Adicione sempre tags básicas aos recursos (Ex: `Environment`, `Project`, `ManagedBy = "Terraform"`).
2. Use variáveis (`variables.tf`) para isolar os parâmetros de ambiente de modo que possamos alternar entre `dev` e `prod` sem alterar a lógica dos módulos centrais.
3. Se o usuário solicitar uma arquitetura que viole qualquer um dos pontos acima, alerte-o imediatamente sobre o risco de segurança ou custo antes de gerar o código.
