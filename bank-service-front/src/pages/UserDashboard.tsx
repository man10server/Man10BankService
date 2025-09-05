import { useCallback, useMemo, useState } from 'react'
import { Api, formatJPY } from '../api/client'
import type { ApiResult, AtmLog, Estate, EstateHistory, Loan, MoneyLog, ServerLoan } from '../api/client'

type FetchState = {
  loading: boolean;
  error?: string;
}

export function UserDashboard() {
  const [uuidInput, setUuidInput] = useState('');
  const [uuid, setUuid] = useState<string | null>(null);

  const [bank, setBank] = useState<ApiResult<number> | null>(null);
  const [estate, setEstate] = useState<ApiResult<Estate> | null>(null);
  const [estateHist, setEstateHist] = useState<ApiResult<EstateHistory[]> | null>(null);
  const [serverLoan, setServerLoan] = useState<ApiResult<ServerLoan> | null>(null);
  const [loans, setLoans] = useState<ApiResult<Loan[]> | null>(null);
  const [bankLogs, setBankLogs] = useState<ApiResult<MoneyLog[]> | null>(null);
  const [atmLogs, setAtmLogs] = useState<ApiResult<AtmLog[]> | null>(null);
  const [state, setState] = useState<FetchState>({ loading: false });

  const onSearch = useCallback(async () => {
    const q = uuidInput.trim();
    if (!q) return;
    setUuid(q);
    setState({ loading: true });
    try {
      const [b, e, eh, sl, lo, bl, al] = await Promise.all([
        Api.bankBalance(q),
        Api.estateLatest(q),
        Api.estateHistory(q, 30, 0),
        Api.serverLoan(q),
        Api.personalLoans(q, 100, 0),
        Api.bankLogs(q, 10, 0),
        Api.atmLogs(q, 10, 0),
      ]);
      setBank(b);
      setEstate(e);
      setEstateHist(eh);
      setServerLoan(sl);
      setLoans(lo);
      setBankLogs(bl);
      setAtmLogs(al);
      setState({ loading: false });
    } catch (err: any) {
      setState({ loading: false, error: err?.message ?? '取得に失敗しました' });
    }
  }, [uuidInput]);

  const title = useMemo(() => (uuid ? `ユーザー: ${uuid}` : 'ユーザーダッシュボード'), [uuid]);

  return (
    <div style={{ padding: 24, maxWidth: 1200, margin: '0 auto', fontFamily: 'system-ui, -apple-system, Segoe UI, Roboto, sans-serif' }}>
      <h1 style={{ marginBottom: 8 }}>{title}</h1>
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <input
          value={uuidInput}
          onChange={(e) => setUuidInput(e.target.value)}
          placeholder="UUID を入力 (例: 00000000-0000-0000-0000-000000000000)"
          style={{ flex: 1, padding: '8px 12px', fontSize: 14 }}
        />
        <button onClick={onSearch} disabled={state.loading} style={{ padding: '8px 16px' }}>
          {state.loading ? '読み込み中…' : '取得'}
        </button>
      </div>
      {state.error && (
        <div style={{ color: '#b00020', marginBottom: 16 }}>エラー: {state.error}</div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(12, 1fr)', gap: 16 }}>
        <BigStatCard span={12}
          cash={estate?.data?.cash}
          vault={estate?.data?.vault}
          bank={estate?.data?.bank ?? (bank?.data ?? null)}
          serverLoan={serverLoan?.data?.borrowAmount}
          total={estate?.data?.total}
          fallbackMsg={estate?.message || serverLoan?.message || bank?.message || ''}
        />

        <Card title="個人間借金" span={6}>
          {loans?.data && loans.data.length > 0 ? (
            <table style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th style={{ textAlign: 'left' }}>貸主</th>
                  <th style={{ textAlign: 'right' }}>金額</th>
                  <th style={{ textAlign: 'left' }}>借入日</th>
                </tr>
              </thead>
              <tbody>
                {loans.data.map(l => (
                  <tr key={l.id}>
                    <td>{l.lendPlayer}</td>
                    <td style={{ textAlign: 'right' }}>{formatJPY(l.amount)}</td>
                    <td>{formatDate(l.borrowDate)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <div>{formatResultMessage(loans)}</div>
          )}
        </Card>

        <BankLogsBigCard span={12} logs={bankLogs?.data ?? []} empty={formatResultMessage(bankLogs)} />

        <Card title="ATM 履歴 (10)" span={6}>
          <AtmLogsTable logs={atmLogs?.data?.slice(0, 10) ?? []} empty={formatResultMessage(atmLogs)} />
        </Card>

        <Card title="資産推移 (30件)" span={12}>
          {estateHist?.data && estateHist.data.length > 0 ? (
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
              {estateHist.data.map(h => (
                <span key={h.id} style={{ fontSize: 12, color: '#333' }}>
                  {formatDate(h.date)}: {formatJPY(h.total)}
                </span>
              ))}
            </div>
          ) : (
            <div>{formatResultMessage(estateHist)}</div>
          )}
        </Card>
      </div>
    </div>
  )
}

function Card(props: { title: string; children: any; span?: number }) {
  const { title, children, span = 4 } = props;
  return (
    <div style={{ gridColumn: `span ${span}`, border: '1px solid #ddd', borderRadius: 8, padding: 12, background: '#fff' }}>
      <div style={{ fontWeight: 600, marginBottom: 8 }}>{title}</div>
      <div>{children}</div>
    </div>
  )
}

function BigStatCard(props: { span?: number; cash: number | null | undefined; vault: number | null | undefined; bank: number | null | undefined; serverLoan: number | null | undefined; total: number | null | undefined; fallbackMsg?: string }) {
  const { span = 12, cash, vault, bank, serverLoan, total, fallbackMsg } = props;
  const hasAny = cash != null || vault != null || bank != null || serverLoan != null || total != null;
  return (
    <div style={{ gridColumn: `span ${span}`, background: '#6b7280', color: '#fff', borderRadius: 12, padding: 16 }}>
      <div style={{ fontSize: 20, fontWeight: 700, marginBottom: 8, textAlign: 'left' }}>サマリー</div>
      {hasAny ? (
        <div style={{ fontSize: '2rem', lineHeight: 1.4, fontWeight: 700, textAlign: 'left' }}>
          <div>現金: {formatJPY(cash ?? 0)}</div>
          <div>電子マネー: {formatJPY(vault ?? 0)}</div>
          <div>銀行: {formatJPY(bank ?? 0)}</div>
          <div>リボ: {formatJPY(serverLoan ?? 0)}</div>
          <div>合計: {formatJPY(total ?? 0)}</div>
        </div>
      ) : (
        <div style={{ fontSize: '2rem', fontWeight: 700, textAlign: 'left' }}>{fallbackMsg || 'データがありません'}</div>
      )}
    </div>
  )
}

function BankLogsBigCard({ logs, empty, span = 12 }: { logs: MoneyLog[]; empty: string; span?: number }) {
  return (
    <div style={{ gridColumn: `span ${span}`, background: '#6b7280', color: '#fff', borderRadius: 12, padding: 16 }}>
      <div style={{ fontSize: 20, fontWeight: 700, marginBottom: 8, textAlign: 'left' }}>Bank 履歴</div>
      <div style={{ maxHeight: 320, overflowY: 'auto', paddingRight: 8 }}>
        {logs && logs.length > 0 ? (
          <table style={{ width: '100%', color: '#fff' }}>
            <thead>
              <tr>
                <th style={{ textAlign: 'left' }}>日時</th>
                <th style={{ textAlign: 'left' }}>内容</th>
                <th style={{ textAlign: 'right' }}>金額</th>
              </tr>
            </thead>
            <tbody>
              {logs.map(l => (
                <tr key={l.id}>
                  <td>{formatDate(l.date)}</td>
                  <td>{l.displayNote}</td>
                  <td style={{ textAlign: 'right' }}>{formatJPY(l.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <div style={{ fontSize: '1rem' }}>{empty || 'データなし'}</div>
        )}
      </div>
    </div>
  )
}

// Bankログ用の大カードに統合したため、従来のLogsTableは削除しました。

function AtmLogsTable({ logs, empty }: { logs: AtmLog[]; empty: string }) {
  if (!logs || logs.length === 0) return <div>{empty}</div>
  return (
    <table style={{ width: '100%' }}>
      <thead>
        <tr>
          <th style={{ textAlign: 'left' }}>日時</th>
          <th style={{ textAlign: 'left' }}>種別</th>
          <th style={{ textAlign: 'right' }}>金額</th>
        </tr>
      </thead>
      <tbody>
        {logs.map(l => (
          <tr key={l.id}>
            <td>{formatDate(l.date)}</td>
            <td>{l.deposit ? '入金' : '出金'}</td>
            <td style={{ textAlign: 'right' }}>{formatJPY(l.amount)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function formatDate(iso?: string | null) {
  if (!iso) return '-';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('ja-JP', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

function formatResultMessage<T>(res?: ApiResult<T> | null) {
  if (!res) return 'データなし';
  if (res.statusCode !== 200) return res.message || '取得できませんでした';
  if (!res.data || (Array.isArray(res.data) && res.data.length === 0)) return 'データなし';
  return '';
}
