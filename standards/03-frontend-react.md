# DIRETRIZES DE FRONTEND (REACT)

## 1. Arquitetura React
- Utilize componentes funcionais e React Hooks. Não gere componentes de classe.
- Mantenha componentes pequenos e focados em uma única responsabilidade.
- Separe componentes de apresentação (UI pura) de componentes contêineres (que buscam dados).

## 2. Integração Backend e Segurança
- O Frontend (React) não deve se conectar diretamente ao Banco de Dados ou a serviços de terceiros sensíveis. Ele DEVE chamar exclusivamente os BFFs/APIs internas.
- **Autenticação:** Utilize a biblioteca oficial da AWS (AWS Amplify Auth ou integração direta Cognito via SDK) para gerenciar o estado do usuário logado.
- Os tokens JWT gerados pelo Cognito (IdToken, AccessToken) devem ser enviados no cabeçalho `Authorization: Bearer <token>` em todas as requisições para o BFF/API.

## 3. Gerenciamento de Estado
- Não complique excessivamente. Utilize Context API para estados globais leves (como tema e usuário autenticado).
- Para *data fetching* (buscas na API), utilize bibliotecas que façam cache e revalidação (como React Query, SWR, ou RTK Query), em vez de fazer `useEffect` com `fetch` manual.