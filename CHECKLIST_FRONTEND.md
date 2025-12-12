# ✅ Checklist de Integração Frontend Angular

Use este checklist para integrar o Dashboard Angular com o Backend Flask já pronto.

---

## 📋 Pré-requisitos

- [ ] Backend Flask rodando em `http://localhost:5000`
- [ ] Projeto Angular criado (Angular 15+)
- [ ] Node.js e npm instalados

---

## 🗂️ Passo 1: Criar Estrutura de Pastas

```bash
cd seu-projeto-angular/src/app

# Criar estrutura core
mkdir -p core/models
mkdir -p core/services
mkdir -p core/interceptors

# Criar estrutura de features
mkdir -p features/dashboard/services
mkdir -p features/dashboard/components
```

---

## 📄 Passo 2: Copiar Models

Copie os arquivos do guia [FRONTEND_ANGULAR_GUIA.md](FRONTEND_ANGULAR_GUIA.md):

- [ ] `src/app/core/models/user.model.ts`
- [ ] `src/app/core/models/dashboard.model.ts`
- [ ] `src/app/core/models/transaction.model.ts`
- [ ] `src/app/core/models/account.model.ts`

**Comando rápido:**
```bash
# Crie os arquivos e cole o conteúdo do guia
touch src/app/core/models/{user,dashboard,transaction,account}.model.ts
```

---

## 🔧 Passo 3: Criar Services

### 3.1 AuthService
- [ ] Criar `src/app/core/services/auth.service.ts`
- [ ] Copiar código do guia
- [ ] Verificar imports (`HttpClient`, `Router`)

### 3.2 ApiService
- [ ] Criar `src/app/core/services/api.service.ts`
- [ ] Copiar código do guia

### 3.3 DashboardService
- [ ] Criar `src/app/features/dashboard/services/dashboard.service.ts`
- [ ] Copiar código do guia

---

## 🔒 Passo 4: Configurar Interceptor

### 4.1 Criar Interceptor
- [ ] Criar `src/app/core/interceptors/auth.interceptor.ts`
- [ ] Copiar código do guia

### 4.2 Registrar no App Config

**Se usando Angular 17+ (standalone):**

Edite `src/app/app.config.ts`:
```typescript
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { AuthInterceptor } from './core/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([AuthInterceptor])
    )
  ]
};
```

**Se usando Angular 15-16 (NgModule):**

Edite `src/app/app.module.ts`:
```typescript
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { AuthInterceptor } from './core/interceptors/auth.interceptor';

@NgModule({
  providers: [
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true
    }
  ]
})
export class AppModule { }
```

- [ ] Interceptor registrado

---

## 🌐 Passo 5: Configurar Environment

### 5.1 Environment de Desenvolvimento
Edite `src/environments/environment.ts`:
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000'  // Backend local
};
```

- [ ] Environment configurado

### 5.2 Environment de Produção
Edite `src/environments/environment.prod.ts`:
```typescript
export const environment = {
  production: true,
  apiUrl: 'https://api.meusecretario.com'  // URL de produção
};
```

- [ ] Environment de produção configurado

---

## 🎨 Passo 6: Criar Components

### 6.1 Login Component (se não existir)
```bash
ng generate component features/auth/login
```

**Template básico:**
```html
<form (ngSubmit)="onLogin()">
  <input type="text" [(ngModel)]="whatsapp" placeholder="WhatsApp" />
  <input type="password" [(ngModel)]="password" placeholder="Senha" />
  <button type="submit">Entrar</button>
</form>
```

**Component:**
```typescript
export class LoginComponent {
  whatsapp = '';
  password = '';

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  onLogin() {
    this.authService.login({ whatsapp: this.whatsapp, password: this.password })
      .subscribe({
        next: (response) => {
          if (response.status === 'success') {
            this.router.navigate(['/dashboard']);
          }
        },
        error: (error) => {
          console.error('Erro ao fazer login:', error);
        }
      });
  }
}
```

- [ ] Login component criado

### 6.2 Dashboard Component
- [ ] Criar `src/app/features/dashboard/components/dashboard.component.ts`
- [ ] Criar `src/app/features/dashboard/components/dashboard.component.html`
- [ ] Copiar código do guia

### 6.3 Transactions Component
- [ ] Criar `src/app/features/dashboard/components/transactions.component.ts`
- [ ] Copiar código do guia

---

## 🛣️ Passo 7: Configurar Rotas

Edite `src/app/app.routes.ts` (ou `app-routing.module.ts`):

```typescript
import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login/login.component';
import { DashboardComponent } from './features/dashboard/components/dashboard.component';
import { TransactionsComponent } from './features/dashboard/components/transactions.component';
import { AuthGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [AuthGuard] },
  { path: 'transactions', component: TransactionsComponent, canActivate: [AuthGuard] },
  { path: '**', redirectTo: '/dashboard' }
];
```

- [ ] Rotas configuradas

---

## 🔐 Passo 8: Criar Auth Guard

Crie `src/app/core/guards/auth.guard.ts`:

```typescript
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const AuthGuard = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  router.navigate(['/login']);
  return false;
};
```

- [ ] Auth guard criado

---

## 🎨 Passo 9: Adicionar CSS (Opcional)

Crie `src/app/features/dashboard/components/dashboard.component.css`:

```css
.dashboard-container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.dashboard-header {
  margin-bottom: 30px;
}

.dashboard-header h1 {
  font-size: 2rem;
  margin-bottom: 5px;
}

.subtitle {
  color: #666;
}

.summary-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 20px;
  margin-bottom: 30px;
}

.card {
  background: white;
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.card-icon {
  font-size: 2rem;
  margin-bottom: 10px;
}

.card h3 {
  font-size: 0.875rem;
  color: #666;
  margin-bottom: 5px;
  font-weight: 500;
}

.value {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0;
}

.text-success { color: #28a745; }
.text-danger { color: #dc3545; }
.text-muted { color: #6c757d; }

.dashboard-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 20px;
  margin-bottom: 30px;
}

@media (max-width: 768px) {
  .dashboard-grid {
    grid-template-columns: 1fr;
  }
}

.transactions-list,
.accounts-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.transaction-item,
.account-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 15px;
  border-bottom: 1px solid #eee;
}

.transaction-item:last-child,
.account-item:last-child {
  border-bottom: none;
}

.transaction-info,
.account-info {
  display: flex;
  flex-direction: column;
}

.transaction-amount,
.account-balance {
  font-weight: 600;
  font-size: 1.125rem;
}

.loading-state,
.empty-state {
  text-align: center;
  padding: 40px 20px;
  color: #666;
}

.btn-secondary {
  background: #6c757d;
  color: white;
  border: none;
  padding: 10px 20px;
  border-radius: 4px;
  cursor: pointer;
  margin-top: 15px;
}

.btn-secondary:hover {
  background: #5a6268;
}

.skeleton {
  background: linear-gradient(90deg, #f0f0f0 25%, #e0e0e0 50%, #f0f0f0 75%);
  background-size: 200% 100%;
  animation: loading 1.5s ease-in-out infinite;
}

@keyframes loading {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
```

- [ ] CSS adicionado

---

## 🧪 Passo 10: Testar

### 10.1 Verificar Backend
```bash
# Terminal 1 - Backend
cd backend
python run.py

# Deve mostrar:
# * Running on http://127.0.0.1:5000
```

- [ ] Backend rodando

### 10.2 Iniciar Frontend
```bash
# Terminal 2 - Frontend
cd frontend
ng serve

# Deve mostrar:
# ** Angular Live Development Server is listening on localhost:4200
```

- [ ] Frontend rodando

### 10.3 Testar Fluxo
1. [ ] Acesse `http://localhost:4200`
2. [ ] Faça login (use WhatsApp e senha cadastrados)
3. [ ] Verifique se o dashboard carrega com dados reais
4. [ ] Teste navegação para transações
5. [ ] Verifique se logout funciona

---

## 🐛 Troubleshooting

### Erro: "Cannot find module '@angular/..."
```bash
npm install
```

### Erro: "NullInjectorError: No provider for HttpClient"
Adicione no `app.config.ts`:
```typescript
import { provideHttpClient } from '@angular/common/http';

providers: [
  provideHttpClient()
]
```

### Erro: "CORS policy: No 'Access-Control-Allow-Origin'"
Verifique se o backend está com CORS habilitado em `.env`:
```
CORS_ENABLED=true
CORS_ORIGINS=http://localhost:4200
```

### Dashboard mostra "R$ 0,00"
Verifique:
1. Token JWT está sendo enviado (Network tab do DevTools)
2. Usuário logado tem transações cadastradas no banco
3. Console do navegador não mostra erros

### Token expirado
Token JWT expira em 24 horas. Faça login novamente.

---

## ✨ Melhorias Opcionais

### Adicionar Loading State Global
```typescript
// loading.service.ts
export class LoadingService {
  private loading = new BehaviorSubject<boolean>(false);
  loading$ = this.loading.asObservable();

  show() { this.loading.next(true); }
  hide() { this.loading.next(false); }
}
```

### Adicionar Toasts/Notificações
```bash
npm install ngx-toastr
```

### Adicionar Gráficos
```bash
npm install chart.js ng2-charts
```

### Adicionar Icons
```bash
npm install @fortawesome/fontawesome-free
```

---

## 📚 Recursos Úteis

- **Documentação Angular:** https://angular.io/docs
- **HttpClient Guide:** https://angular.io/guide/http
- **RxJS Operators:** https://rxjs.dev/guide/operators
- **Angular Material:** https://material.angular.io (componentes prontos)

---

## 🎉 Finalização

Quando completar todos os itens:

- [ ] Dashboard carregando dados reais
- [ ] Login/Logout funcionando
- [ ] Transações exibindo corretamente
- [ ] Sem erros no console
- [ ] Interceptor JWT funcionando

**Parabéns! Sua integração está completa!** 🚀

---

**Última atualização:** 2025-12-12
