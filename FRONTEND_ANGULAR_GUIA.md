# Guia de Integração - Frontend Angular

Este guia contém todos os arquivos TypeScript necessários para integrar o Dashboard Angular com o Backend Flask.

## 📋 Status do Backend

✅ **Backend 100% Pronto!** Todos os endpoints REST estão implementados e funcionando:

### Autenticação
- `POST /auth/login` - Autenticação (retorna JWT)
- `POST /auth/register` - Registro de novo usuário
- `POST /auth/verify` - Verificar se token é válido

### Dashboard
- `GET /api/dashboard/summary` - Resumo financeiro (saldo, receitas, despesas)
- `GET /api/dashboard/stats` - Alias para summary
- `GET /api/dashboard/charts` - Dados para gráficos (gastos mensais, por categoria)
- `GET /api/dashboard/recent` - Transações recentes (para dashboard)

### Transações (CRUD Completo)
- `GET /api/transactions` - Lista de transações com filtros e paginação
- `GET /api/transactions/recent` - Últimas 10 transações
- `POST /api/transactions` - **NOVO** Criar nova transação
- `PUT /api/transactions/:id` - **NOVO** Atualizar transação existente
- `DELETE /api/transactions/:id` - **NOVO** Deletar transação

### Contas
- `GET /api/accounts` - Contas do usuário com saldos

### Categorias
- `GET /api/categories` - **NOVO** Lista categorias para forms (filtro: ?tipo=Receita|Despesa)

### Health Check
- `GET /api/health` - Verificar se API está online

---

## 🗂️ Estrutura de Pastas Angular

```
src/app/
├── core/
│   ├── models/
│   │   ├── user.model.ts
│   │   ├── dashboard.model.ts
│   │   ├── transaction.model.ts
│   │   └── account.model.ts
│   ├── services/
│   │   ├── auth.service.ts
│   │   └── api.service.ts
│   └── interceptors/
│       └── auth.interceptor.ts
└── features/
    └── dashboard/
        ├── services/
        │   └── dashboard.service.ts
        ├── components/
        │   ├── dashboard.component.ts
        │   ├── dashboard.component.html
        │   └── transactions.component.ts
        └── dashboard-routing.module.ts
```

---

## 📄 Arquivos TypeScript

### 1. Models (src/app/core/models/)

#### user.model.ts
```typescript
export interface User {
  id: number;
  nome: string;
  whatsapp: string;
}

export interface LoginRequest {
  whatsapp: string;
  password: string;
}

export interface LoginResponse {
  status: 'success' | 'error';
  token?: string;
  user?: User;
  message?: string;
}

export interface RegisterRequest {
  nome: string;
  whatsapp: string;
  password: string;
  dia_vencimento: number;
  dia_fechamento: number;
}

export interface RegisterResponse {
  status: 'success' | 'error';
  message: string;
  user_id?: number;
}
```

#### dashboard.model.ts
```typescript
export interface DashboardSummary {
  saldo_total: number;
  receitas_mes: number;
  despesas_mes: number;
  saldo_mes: number;
  mes_referencia: string;
}

export interface DashboardSummaryResponse {
  status: 'success' | 'error';
  data?: DashboardSummary;
  message?: string;
}

export interface ChartData {
  gastos_mensais: GastoMensal[];
  gastos_categoria: GastoCategoria[];
  gastos_dia_semana: GastoDiaSemana[];
}

export interface GastoMensal {
  mes: string;  // YYYY-MM
  total: number;
}

export interface GastoCategoria {
  macro_categoria: string;
  subcategoria: string;
  total: number;
  quantidade: number;
}

export interface GastoDiaSemana {
  dia: string;
  total: number;
  quantidade: number;
}

export interface ChartDataResponse {
  status: 'success' | 'error';
  data?: ChartData;
  message?: string;
}
```

#### transaction.model.ts
```typescript
export interface Transaction {
  id: number;
  descricao: string;
  valor: number;
  tipo: 'Receita' | 'Despesa';
  data: string;  // ISO format
  categoria: string;
  subcategoria?: string;
  conta: string;
  consolidada?: boolean;
}

export interface TransactionListResponse {
  status: 'success' | 'error';
  data?: {
    total: number;
    limit: number;
    offset: number;
    transactions: Transaction[];
  };
  message?: string;
}

export interface TransactionRecentResponse {
  status: 'success' | 'error';
  data?: Transaction[];
  message?: string;
}
```

#### account.model.ts
```typescript
export interface Account {
  nome_conta: string;
  tipo_conta: string;
  saldo: number;
}

export interface AccountListResponse {
  status: 'success' | 'error';
  data?: Account[];
  message?: string;
}
```

#### category.model.ts (NOVO)
```typescript
export interface SubCategory {
  id: number;
  nome: string;
}

export interface Category {
  grupo: string;
  macro_id: number;
  macro_categoria: string;
  subcategorias: SubCategory[];
}

export interface CategoryListResponse {
  status: 'success' | 'error';
  data?: Category[];
  message?: string;
}

// Request para criar transação
export interface CreateTransactionRequest {
  descricao: string;
  valor: number;
  tipo: 'Receita' | 'Despesa';
  data: string;  // YYYY-MM-DD
  subcategoria_id: number;
  conta_id: number;
  observacoes?: string;
  consolidada?: boolean;
}

// Request para atualizar transação (todos campos opcionais)
export interface UpdateTransactionRequest {
  descricao?: string;
  valor?: number;
  tipo?: 'Receita' | 'Despesa';
  data?: string;
  subcategoria_id?: number;
  conta_id?: number;
  observacoes?: string;
  consolidada?: boolean;
}

// Response de criação/atualização
export interface TransactionMutationResponse {
  status: 'success' | 'error';
  message: string;
  data?: {
    id: number;
    descricao: string;
    valor: number;
    tipo: string;
    data: string;
  };
}

// Response de deleção
export interface DeleteResponse {
  status: 'success' | 'error';
  message: string;
}
```

---

### 2. Core Services (src/app/core/services/)

#### auth.service.ts
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';
import { LoginRequest, LoginResponse, RegisterRequest, RegisterResponse, User } from '../models/user.model';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = environment.apiUrl || 'http://localhost:5000';
  private readonly TOKEN_KEY = 'jwt_token';
  private readonly USER_KEY = 'current_user';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getUserFromStorage());
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  /**
   * Faz login e armazena token + user no localStorage
   */
  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.API_URL}/auth/login`, credentials)
      .pipe(
        tap(response => {
          if (response.status === 'success' && response.token && response.user) {
            this.setToken(response.token);
            this.setUser(response.user);
            this.currentUserSubject.next(response.user);
          }
        })
      );
  }

  /**
   * Registra novo usuário
   */
  register(data: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.API_URL}/auth/register`, data);
  }

  /**
   * Faz logout, limpa storage e redireciona
   */
  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  /**
   * Retorna o token armazenado
   */
  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  /**
   * Verifica se o usuário está autenticado
   */
  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  /**
   * Retorna o usuário atual
   */
  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  // Métodos privados

  private setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  private setUser(user: User): void {
    localStorage.setItem(this.USER_KEY, JSON.stringify(user));
  }

  private getUserFromStorage(): User | null {
    const userJson = localStorage.getItem(this.USER_KEY);
    if (userJson) {
      try {
        return JSON.parse(userJson);
      } catch {
        return null;
      }
    }
    return null;
  }
}
```

#### api.service.ts
```typescript
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/**
 * Serviço base para chamadas HTTP à API
 * Injeta automaticamente o token via interceptor
 */
@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly API_URL = environment.apiUrl || 'http://localhost:5000';

  constructor(private http: HttpClient) {}

  /**
   * GET request genérico
   */
  get<T>(endpoint: string, params?: any): Observable<T> {
    let httpParams = new HttpParams();

    if (params) {
      Object.keys(params).forEach(key => {
        if (params[key] !== null && params[key] !== undefined) {
          httpParams = httpParams.set(key, params[key].toString());
        }
      });
    }

    return this.http.get<T>(`${this.API_URL}${endpoint}`, { params: httpParams });
  }

  /**
   * POST request genérico
   */
  post<T>(endpoint: string, body: any): Observable<T> {
    return this.http.post<T>(`${this.API_URL}${endpoint}`, body);
  }

  /**
   * PUT request genérico
   */
  put<T>(endpoint: string, body: any): Observable<T> {
    return this.http.put<T>(`${this.API_URL}${endpoint}`, body);
  }

  /**
   * DELETE request genérico
   */
  delete<T>(endpoint: string): Observable<T> {
    return this.http.delete<T>(`${this.API_URL}${endpoint}`);
  }
}
```

---

### 3. Interceptor (src/app/core/interceptors/)

#### auth.interceptor.ts
```typescript
import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';
import { Router } from '@angular/router';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Pegar token do AuthService
    const token = this.authService.getToken();

    // Clonar request e adicionar Authorization header
    let authReq = req;
    if (token) {
      authReq = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }

    // Enviar request e tratar erros
    return next.handle(authReq).pipe(
      catchError((error: HttpErrorResponse) => {
        // Se 401 (não autorizado), fazer logout
        if (error.status === 401) {
          this.authService.logout();
        }

        return throwError(() => error);
      })
    );
  }
}
```

**Registrar o interceptor em `app.config.ts` ou `app.module.ts`:**

```typescript
// app.config.ts (Angular 17+)
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { AuthInterceptor } from './core/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    // ... outros providers
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true
    }
  ]
};
```

---

### 4. Dashboard Service (src/app/features/dashboard/services/)

#### dashboard.service.ts
```typescript
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  DashboardSummaryResponse,
  ChartDataResponse
} from '../../../core/models/dashboard.model';
import {
  TransactionRecentResponse,
  TransactionListResponse,
  CreateTransactionRequest,
  UpdateTransactionRequest,
  TransactionMutationResponse,
  DeleteResponse
} from '../../../core/models/transaction.model';
import { AccountListResponse } from '../../../core/models/account.model';
import { CategoryListResponse } from '../../../core/models/category.model';

@Injectable({
  providedIn: 'root'
})
export class DashboardService {
  constructor(private api: ApiService) {}

  // ==================== DASHBOARD ====================

  /**
   * Busca resumo do dashboard
   * GET /api/dashboard/summary
   */
  getSummary(): Observable<DashboardSummaryResponse> {
    return this.api.get<DashboardSummaryResponse>('/api/dashboard/summary');
  }

  /**
   * Busca transações recentes para o dashboard
   * GET /api/dashboard/recent
   */
  getRecentTransactions(): Observable<TransactionRecentResponse> {
    return this.api.get<TransactionRecentResponse>('/api/dashboard/recent');
  }

  /**
   * Busca dados para gráficos
   * GET /api/dashboard/charts?meses=3
   */
  getChartData(meses: number = 3): Observable<ChartDataResponse> {
    return this.api.get<ChartDataResponse>('/api/dashboard/charts', { meses });
  }

  // ==================== TRANSAÇÕES (CRUD) ====================

  /**
   * Busca lista completa de transações com filtros
   * GET /api/transactions
   */
  getTransactions(params?: {
    limit?: number;
    offset?: number;
    tipo?: 'Receita' | 'Despesa';
    data_inicio?: string;
    data_fim?: string;
  }): Observable<TransactionListResponse> {
    return this.api.get<TransactionListResponse>('/api/transactions', params);
  }

  /**
   * Cria uma nova transação
   * POST /api/transactions
   */
  createTransaction(transaction: CreateTransactionRequest): Observable<TransactionMutationResponse> {
    return this.api.post<TransactionMutationResponse>('/api/transactions', transaction);
  }

  /**
   * Atualiza uma transação existente
   * PUT /api/transactions/:id
   */
  updateTransaction(id: number, transaction: UpdateTransactionRequest): Observable<TransactionMutationResponse> {
    return this.api.put<TransactionMutationResponse>(`/api/transactions/${id}`, transaction);
  }

  /**
   * Deleta uma transação
   * DELETE /api/transactions/:id
   */
  deleteTransaction(id: number): Observable<DeleteResponse> {
    return this.api.delete<DeleteResponse>(`/api/transactions/${id}`);
  }

  // ==================== CONTAS ====================

  /**
   * Busca contas do usuário
   * GET /api/accounts
   */
  getAccounts(): Observable<AccountListResponse> {
    return this.api.get<AccountListResponse>('/api/accounts');
  }

  // ==================== CATEGORIAS ====================

  /**
   * Busca categorias disponíveis
   * GET /api/categories?tipo=Receita|Despesa
   */
  getCategories(tipo?: 'Receita' | 'Despesa'): Observable<CategoryListResponse> {
    const params = tipo ? { tipo } : undefined;
    return this.api.get<CategoryListResponse>('/api/categories', params);
  }
}
```

---

### 5. Dashboard Component (src/app/features/dashboard/components/)

#### dashboard.component.ts
```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DashboardService } from '../services/dashboard.service';
import { AuthService } from '../../../core/services/auth.service';
import { DashboardSummary, ChartData } from '../../../core/models/dashboard.model';
import { Transaction } from '../../../core/models/transaction.model';
import { Account } from '../../../core/models/account.model';
import { finalize } from 'rxjs/operators';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit {
  // Dados
  summary: DashboardSummary | null = null;
  recentTransactions: Transaction[] = [];
  accounts: Account[] = [];
  chartData: ChartData | null = null;

  // Loading states
  loadingSummary = true;
  loadingTransactions = true;
  loadingAccounts = true;
  loadingCharts = true;

  // Usuário logado
  userName: string = '';

  constructor(
    private dashboardService: DashboardService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    // Pegar nome do usuário logado
    const user = this.authService.getCurrentUser();
    this.userName = user?.nome || 'Usuário';

    // Carregar dados
    this.loadSummary();
    this.loadRecentTransactions();
    this.loadAccounts();
    this.loadChartData();
  }

  /**
   * Carrega resumo financeiro
   */
  loadSummary(): void {
    this.loadingSummary = true;
    this.dashboardService.getSummary()
      .pipe(finalize(() => this.loadingSummary = false))
      .subscribe({
        next: (response) => {
          if (response.status === 'success' && response.data) {
            this.summary = response.data;
          }
        },
        error: (error) => {
          console.error('Erro ao carregar resumo:', error);
        }
      });
  }

  /**
   * Carrega transações recentes
   */
  loadRecentTransactions(): void {
    this.loadingTransactions = true;
    this.dashboardService.getRecentTransactions()
      .pipe(finalize(() => this.loadingTransactions = false))
      .subscribe({
        next: (response) => {
          if (response.status === 'success' && response.data) {
            this.recentTransactions = response.data;
          }
        },
        error: (error) => {
          console.error('Erro ao carregar transações:', error);
        }
      });
  }

  /**
   * Carrega contas do usuário
   */
  loadAccounts(): void {
    this.loadingAccounts = true;
    this.dashboardService.getAccounts()
      .pipe(finalize(() => this.loadingAccounts = false))
      .subscribe({
        next: (response) => {
          if (response.status === 'success' && response.data) {
            this.accounts = response.data;
          }
        },
        error: (error) => {
          console.error('Erro ao carregar contas:', error);
        }
      });
  }

  /**
   * Carrega dados para gráficos
   */
  loadChartData(): void {
    this.loadingCharts = true;
    this.dashboardService.getChartData(3)
      .pipe(finalize(() => this.loadingCharts = false))
      .subscribe({
        next: (response) => {
          if (response.status === 'success' && response.data) {
            this.chartData = response.data;
          }
        },
        error: (error) => {
          console.error('Erro ao carregar dados dos gráficos:', error);
        }
      });
  }

  /**
   * Formata valor para moeda brasileira
   */
  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', {
      style: 'currency',
      currency: 'BRL'
    }).format(value);
  }

  /**
   * Formata data para formato brasileiro
   */
  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('pt-BR');
  }

  /**
   * Retorna classe CSS baseada no tipo de transação
   */
  getTransactionClass(tipo: string): string {
    return tipo === 'Receita' ? 'text-success' : 'text-danger';
  }
}
```

#### dashboard.component.html
```html
<div class="dashboard-container">
  <!-- Header -->
  <header class="dashboard-header">
    <h1>Olá, {{ userName }}!</h1>
    <p class="subtitle">Aqui está o resumo das suas finanças</p>
  </header>

  <!-- Cards de Resumo -->
  <section class="summary-cards" *ngIf="!loadingSummary && summary">
    <div class="card">
      <div class="card-icon">💰</div>
      <div class="card-content">
        <h3>Saldo Total</h3>
        <p class="value">{{ formatCurrency(summary.saldo_total) }}</p>
      </div>
    </div>

    <div class="card">
      <div class="card-icon">📈</div>
      <div class="card-content">
        <h3>Receitas {{ summary.mes_referencia }}</h3>
        <p class="value text-success">{{ formatCurrency(summary.receitas_mes) }}</p>
      </div>
    </div>

    <div class="card">
      <div class="card-icon">📉</div>
      <div class="card-content">
        <h3>Despesas {{ summary.mes_referencia }}</h3>
        <p class="value text-danger">{{ formatCurrency(summary.despesas_mes) }}</p>
      </div>
    </div>

    <div class="card">
      <div class="card-icon">💵</div>
      <div class="card-content">
        <h3>Saldo do Mês</h3>
        <p class="value" [ngClass]="summary.saldo_mes >= 0 ? 'text-success' : 'text-danger'">
          {{ formatCurrency(summary.saldo_mes) }}
        </p>
      </div>
    </div>
  </section>

  <!-- Loading state -->
  <section class="summary-cards" *ngIf="loadingSummary">
    <div class="card skeleton" *ngFor="let i of [1,2,3,4]"></div>
  </section>

  <!-- Grid principal -->
  <div class="dashboard-grid">
    <!-- Transações Recentes -->
    <section class="card transactions-section">
      <h2>Transações Recentes</h2>

      <div *ngIf="loadingTransactions" class="loading-state">
        Carregando transações...
      </div>

      <div *ngIf="!loadingTransactions && recentTransactions.length === 0" class="empty-state">
        Nenhuma transação encontrada
      </div>

      <ul class="transactions-list" *ngIf="!loadingTransactions && recentTransactions.length > 0">
        <li *ngFor="let transaction of recentTransactions" class="transaction-item">
          <div class="transaction-info">
            <strong>{{ transaction.descricao }}</strong>
            <small class="text-muted">
              {{ transaction.categoria }} • {{ formatDate(transaction.data) }}
            </small>
          </div>
          <div class="transaction-amount" [ngClass]="getTransactionClass(transaction.tipo)">
            {{ formatCurrency(Math.abs(transaction.valor)) }}
          </div>
        </li>
      </ul>

      <button class="btn-secondary" routerLink="/transactions">Ver Todas</button>
    </section>

    <!-- Contas -->
    <section class="card accounts-section">
      <h2>Minhas Contas</h2>

      <div *ngIf="loadingAccounts" class="loading-state">
        Carregando contas...
      </div>

      <div *ngIf="!loadingAccounts && accounts.length === 0" class="empty-state">
        Nenhuma conta encontrada
      </div>

      <ul class="accounts-list" *ngIf="!loadingAccounts && accounts.length > 0">
        <li *ngFor="let account of accounts" class="account-item">
          <div class="account-info">
            <strong>{{ account.nome_conta }}</strong>
            <small class="text-muted">{{ account.tipo_conta }}</small>
          </div>
          <div class="account-balance" [ngClass]="account.saldo >= 0 ? 'text-success' : 'text-danger'">
            {{ formatCurrency(account.saldo) }}
          </div>
        </li>
      </ul>
    </section>
  </div>

  <!-- Gráficos (futuro) -->
  <section class="card charts-section" *ngIf="!loadingCharts && chartData">
    <h2>Análise de Gastos</h2>
    <p class="text-muted">Gráficos em desenvolvimento...</p>

    <!-- Debug: Mostrar dados -->
    <details>
      <summary>Dados dos Gráficos (Debug)</summary>
      <pre>{{ chartData | json }}</pre>
    </details>
  </section>
</div>
```

---

### 6. Transactions Component (src/app/features/dashboard/components/)

#### transactions.component.ts
```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DashboardService } from '../services/dashboard.service';
import { Transaction } from '../../../core/models/transaction.model';
import { finalize } from 'rxjs/operators';

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="transactions-container">
      <header>
        <h1>Transações</h1>
      </header>

      <!-- Filtros -->
      <section class="filters">
        <select [(ngModel)]="filterTipo" (change)="loadTransactions()">
          <option value="">Todos os Tipos</option>
          <option value="Receita">Receitas</option>
          <option value="Despesa">Despesas</option>
        </select>

        <input
          type="date"
          [(ngModel)]="filterDataInicio"
          (change)="loadTransactions()"
          placeholder="Data Início"
        />

        <input
          type="date"
          [(ngModel)]="filterDataFim"
          (change)="loadTransactions()"
          placeholder="Data Fim"
        />

        <button (click)="clearFilters()">Limpar Filtros</button>
      </section>

      <!-- Loading -->
      <div *ngIf="loading" class="loading-state">
        Carregando transações...
      </div>

      <!-- Tabela -->
      <table class="transactions-table" *ngIf="!loading">
        <thead>
          <tr>
            <th>Data</th>
            <th>Descrição</th>
            <th>Categoria</th>
            <th>Conta</th>
            <th>Valor</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let transaction of transactions">
            <td>{{ formatDate(transaction.data) }}</td>
            <td>{{ transaction.descricao }}</td>
            <td>{{ transaction.categoria }}</td>
            <td>{{ transaction.conta }}</td>
            <td [ngClass]="getTransactionClass(transaction.tipo)">
              {{ formatCurrency(Math.abs(transaction.valor)) }}
            </td>
          </tr>
        </tbody>
      </table>

      <!-- Paginação -->
      <footer class="pagination" *ngIf="!loading">
        <button
          (click)="previousPage()"
          [disabled]="currentPage === 0"
        >
          Anterior
        </button>
        <span>Página {{ currentPage + 1 }} de {{ totalPages }}</span>
        <button
          (click)="nextPage()"
          [disabled]="currentPage >= totalPages - 1"
        >
          Próxima
        </button>
      </footer>
    </div>
  `
})
export class TransactionsComponent implements OnInit {
  transactions: Transaction[] = [];
  loading = true;

  // Paginação
  currentPage = 0;
  pageSize = 20;
  totalRecords = 0;

  // Filtros
  filterTipo: string = '';
  filterDataInicio: string = '';
  filterDataFim: string = '';

  constructor(private dashboardService: DashboardService) {}

  ngOnInit(): void {
    this.loadTransactions();
  }

  loadTransactions(): void {
    this.loading = true;

    const params: any = {
      limit: this.pageSize,
      offset: this.currentPage * this.pageSize
    };

    if (this.filterTipo) {
      params.tipo = this.filterTipo;
    }

    if (this.filterDataInicio) {
      params.data_inicio = this.filterDataInicio;
    }

    if (this.filterDataFim) {
      params.data_fim = this.filterDataFim;
    }

    this.dashboardService.getTransactions(params)
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: (response) => {
          if (response.status === 'success' && response.data) {
            this.transactions = response.data.transactions;
            this.totalRecords = response.data.total;
          }
        },
        error: (error) => {
          console.error('Erro ao carregar transações:', error);
        }
      });
  }

  nextPage(): void {
    this.currentPage++;
    this.loadTransactions();
  }

  previousPage(): void {
    this.currentPage--;
    this.loadTransactions();
  }

  clearFilters(): void {
    this.filterTipo = '';
    this.filterDataInicio = '';
    this.filterDataFim = '';
    this.currentPage = 0;
    this.loadTransactions();
  }

  get totalPages(): number {
    return Math.ceil(this.totalRecords / this.pageSize);
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', {
      style: 'currency',
      currency: 'BRL'
    }).format(value);
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('pt-BR');
  }

  getTransactionClass(tipo: string): string {
    return tipo === 'Receita' ? 'text-success' : 'text-danger';
  }
}
```

---

### 7. Environment Configuration

#### environments/environment.ts
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000'
};
```

#### environments/environment.prod.ts
```typescript
export const environment = {
  production: true,
  apiUrl: 'https://api.meusecretario.com'  // URL de produção
};
```

---

## 🧪 Testando a Integração

### 1. Instalar Dependências
```bash
npm install
```

### 2. Configurar Environment
Edite `src/environments/environment.ts` e configure a URL da API:
```typescript
apiUrl: 'http://localhost:5000'
```

### 3. Iniciar Angular
```bash
ng serve
```

### 4. Testar Fluxo Completo

1. **Acesse** `http://localhost:4200/login`
2. **Faça login** com WhatsApp e senha
3. **Veja o dashboard** com dados reais do backend
4. **Navegue** para transações

---

## 🔒 Segurança

### Checklist de Segurança

✅ **Token JWT** armazenado no localStorage
✅ **Interceptor** adiciona token automaticamente
✅ **Logout automático** em caso de 401
✅ **CORS** configurado no backend
✅ **HTTPS** obrigatório em produção

### Melhorias Futuras

- [ ] Implementar refresh token
- [ ] Armazenar token em HttpOnly cookie (mais seguro)
- [ ] Adicionar rate limiting no frontend
- [ ] Implementar CSP (Content Security Policy)

---

## 🐛 Troubleshooting

### Erro: CORS blocked
**Solução:** Configure CORS no backend Flask (já está configurado em `app/__init__.py`)

### Erro: 401 Unauthorized
**Solução:** Verifique se o token está sendo enviado no header `Authorization: Bearer <token>`

### Erro: 404 Not Found
**Solução:** Verifique se o endpoint está correto e se o backend está rodando

### Dashboard mostra "R$ 0,00"
**Solução:** Verifique se há transações cadastradas no banco de dados

---

## 📚 Referências

- [Angular HttpClient](https://angular.io/guide/http)
- [RxJS Observables](https://rxjs.dev/guide/observable)
- [Angular Interceptors](https://angular.io/guide/http#intercepting-requests-and-responses)
- [Flask CORS](https://flask-cors.readthedocs.io/)

---

**Implementado em**: 2025-12-12
**Versão**: 1.0
**Status**: ✅ Pronto para integração
