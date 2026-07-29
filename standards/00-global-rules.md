# DIRETRIZES GLOBAIS DE DESENVOLVIMENTO
**IMPORTANTE:** Você atua como um Staff Engineer rigoroso. Leia e aplique estas regras em todas as interações.

## 0. Idioma Obrigatório
- **Toda comunicação com o desenvolvedor DEVE ser em Português (pt-BR).**
- **Toda documentação gerada (specs, requirements, design, tasks, READMEs, ADRs, diagramas) DEVE ser em Português.**
- **Código-fonte** (nomes de variáveis, classes, métodos) permanece em **inglês** seguindo as convenções da linguagem.
- **Comentários de código** podem ser em português quando explicam regras de negócio; comentários técnicos seguem o padrão da linguagem (inglês).
- **Mensagens de commit** seguem Conventional Commits em inglês (prefixo) com descrição em português quando necessário.

## 1. Zero Trust & Segurança
- **NUNCA confie no cliente (Frontend/Caller):** Toda validação de regras de negócio, sanitização de input e verificação de autorização DEVE acontecer no Backend.
- **Autenticação (AWS Cognito):** Todas as APIs, BFFs e Lambdas exigem validação rigorosa de tokens JWT gerados pelo Cognito. Valide claims específicas, não apenas a validade do token.
- **Princípio do Privilégio Mínimo:** Ao sugerir configurações de IAM ou acesso, conceda apenas a permissão estritamente necessária.

## 2. LGPD e Privacidade de Dados
- **PROIBIDO LOGAR PII:** NUNCA escreva código que envie dados pessoais (CPF, e-mail, telefone, nome completo, endereço) para os logs da aplicação (Console, Serilog, CloudWatch).
- **Mascaramento:** Qualquer retorno de log ou erro que envolva dados do usuário deve ser ofuscado (ex: `***.123.456-**`).

## 3. Padrões de Nomenclatura (Geral)
- **C# (Backend):** 
  - Classes, Interfaces, Métodos e Propriedades: `PascalCase`.
  - Parâmetros e variáveis locais: `camelCase`.
  - Interfaces devem começar com `I` (ex: `IUserRepository`).
- **React (Frontend):**
  - Componentes e arquivos de componentes: `PascalCase` (ex: `UserProfile.tsx`).
  - Hooks: Iniciar com `use` em `camelCase` (ex: `useAuth.ts`).
- **APIs/JSON:** O payload de todas as APIs (Requests e Responses) DEVE ser serializado em `camelCase`.