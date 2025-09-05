export type ApiResult<T> = {
  statusCode: number;
  message: string;
  data: T | null;
};

export type MoneyLog = {
  id: number;
  player: string;
  uuid: string;
  pluginName: string;
  amount: number;
  note: string;
  displayNote: string;
  server: string;
  deposit: boolean;
  date: string; // ISO
};

export type AtmLog = {
  id: number;
  player: string;
  uuid: string;
  amount: number;
  deposit: boolean;
  date: string; // ISO
};

export type Estate = {
  id: number;
  player: string;
  uuid: string;
  date: string; // ISO
  vault: number;
  bank: number;
  cash: number;
  estateAmount: number;
  loan: number;
  shop: number;
  crypto: number;
  total: number;
};

export type EstateHistory = Estate;

export type ServerLoan = {
  id: number;
  player: string;
  uuid: string;
  borrowDate: string; // ISO
  lastPayDate: string; // ISO
  borrowAmount: number;
  paymentAmount: number;
  failedPayment: number;
  stopInterest: boolean;
};

export type Loan = {
  id: number;
  lendPlayer: string;
  lendUuid: string;
  borrowPlayer: string;
  borrowUuid: string;
  borrowDate: string; // ISO
  paybackDate: string; // ISO
  amount: number;
  collateralItem?: string | null;
};

async function get<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  const res = await fetch(path, {
    headers: { 'Accept': 'application/json' },
    ...init,
  });
  // ASP.NET returns ApiResult<T>
  const json = await res.json().catch(() => null);
  if (!json) {
    return { statusCode: res.status, message: '無効なレスポンス', data: null };
  }
  // normalize keys possibly different casing
  return {
    statusCode: json.statusCode ?? json.StatusCode ?? res.status,
    message: json.message ?? json.Message ?? '',
    data: (json.data ?? json.Data ?? null) as T | null,
  };
}

export const Api = {
  bankBalance: (uuid: string) => get<number>(`/api/Bank/${encodeURIComponent(uuid)}/balance`),
  bankLogs: (uuid: string, limit = 10, offset = 0) =>
    get<MoneyLog[]>(`/api/Bank/${encodeURIComponent(uuid)}/logs?limit=${limit}&offset=${offset}`),
  estateLatest: (uuid: string) => get<Estate>(`/api/Estate/${encodeURIComponent(uuid)}`),
  estateHistory: (uuid: string, limit = 30, offset = 0) =>
    get<EstateHistory[]>(`/api/Estate/${encodeURIComponent(uuid)}/history?limit=${limit}&offset=${offset}`),
  serverLoan: (uuid: string) => get<ServerLoan>(`/api/ServerLoan/${encodeURIComponent(uuid)}`),
  personalLoans: (uuid: string, limit = 100, offset = 0) =>
    get<Loan[]>(`/api/Loan/borrower/${encodeURIComponent(uuid)}?limit=${limit}&offset=${offset}`),
  atmLogs: (uuid: string, limit = 10, offset = 0) =>
    get<AtmLog[]>(`/api/Atm/${encodeURIComponent(uuid)}/logs?limit=${limit}&offset=${offset}`),
};

export function formatJPY(n: number | null | undefined) {
  if (n == null) return '-';
  return new Intl.NumberFormat('ja-JP', { style: 'currency', currency: 'JPY', maximumFractionDigits: 0 }).format(n);
}

