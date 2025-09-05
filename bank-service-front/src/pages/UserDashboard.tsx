import { useCallback, useMemo, useRef, useState } from 'react'
import { Api, formatJPY } from '../api/client'
import type { ApiResult, Estate, EstateHistory, MoneyLog, ServerLoan } from '../api/client'

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
  const [bankLogs, setBankLogs] = useState<ApiResult<MoneyLog[]> | null>(null);
  // ATMログは表示しないため削除
  const [state, setState] = useState<FetchState>({ loading: false });

  const onSearch = useCallback(async () => {
    const q = uuidInput.trim();
    if (!q) return;
    setUuid(q);
    setState({ loading: true });
    try {
      const [b, e, eh, sl, bl] = await Promise.all([
        Api.bankBalance(q),
        Api.estateLatest(q),
        Api.estateHistory(q, 30, 0),
        Api.serverLoan(q),
        Api.bankLogs(q, 10, 0),
      ]);
      setBank(b);
      setEstate(e);
      setEstateHist(eh);
      setServerLoan(sl);
      setBankLogs(bl);
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
        <BankLogsBigCard span={12} logs={bankLogs?.data ?? []} empty={formatResultMessage(bankLogs)} />

        <EstateHistoryChartCard span={12} data={estateHist?.data ?? []} empty={formatResultMessage(estateHist)} />
      </div>
    </div>
  )
}

// 白カードの汎用コンポーネントは現在未使用のため削除

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
                <th style={{ textAlign: 'left' }}>金額</th>
              </tr>
            </thead>
            <tbody>
              {
              logs.map(l => (
                <tr key={l.id}>
                  <td style={{ textAlign: 'left' }}>{formatDate(l.date)}</td>
                  <td style={{ textAlign: 'left' }}>{l.displayNote}</td>
                  <td style={{ textAlign: 'left', color: l.deposit ? 'yellowgreen' : '#ff4d4f' }}>{formatJPY(l.amount)}</td>
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

function EstateHistoryChartCard({ data, empty, span = 12 }: { data: EstateHistory[]; empty: string; span?: number }) {
  const width = 1100; // container max is 1200 with padding
  const height = 280;
  const padding = { top: 16, right: 16, bottom: 36, left: 64 };
  const innerW = width - padding.left - padding.right;
  const innerH = height - padding.top - padding.bottom;

  const totals = useMemo(() => (data && data.length ? data.map(d => Number(d.total)) : []), [data]);
  const min = useMemo(() => (totals.length ? Math.min(...totals) : 0), [totals]);
  const max = useMemo(() => (totals.length ? Math.max(...totals) : 1), [totals]);
  const range = Math.max(1, max - min);

  const points = useMemo(() => {
    if (!data || data.length === 0) return '';
    return data
      .map((d, i) => {
        const x = padding.left + (innerW * i) / Math.max(1, data.length - 1);
        const norm = (Number(d.total) - min) / range;
        const y = padding.top + (1 - norm) * innerH;
        return `${x},${y}`;
      })
      .join(' ');
  }, [data, innerW, innerH, min, range, padding.left, padding.top]);

  // Axis ticks
  const yTickCount = 4;
  const yTicks = useMemo(() => {
    return Array.from({ length: yTickCount + 1 }, (_, i) => {
      const value = min + (range * i) / yTickCount;
      const norm = (value - min) / range;
      const y = padding.top + (1 - norm) * innerH;
      return { y, value };
    });
  }, [min, range, innerH, padding.top]);

  const xTickCount = Math.min(6, Math.max(2, Math.floor((data?.length ?? 0) / 5)) + 1);
  const xTicks = useMemo(() => {
    if (!data || data.length === 0) return [] as { x: number; label: string }[];
    const n = data.length - 1;
    const formatter = new Intl.DateTimeFormat('ja-JP', { month: 'numeric', day: 'numeric' });
    return Array.from({ length: xTickCount }, (_, i) => {
      const idx = Math.round((n * i) / Math.max(1, xTickCount - 1));
      const x = padding.left + (innerW * idx) / Math.max(1, n);
      const label = formatter.format(new Date(data[idx].date));
      return { x, label };
    });
  }, [data, innerW, padding.left, xTickCount]);

  // Hover state
  const svgRef = useRef<SVGSVGElement | null>(null);
  const [hover, setHover] = useState<{ idx: number; x: number; y: number } | null>(null);

  const handleMove = useCallback(
    (e: React.MouseEvent<SVGSVGElement>) => {
      if (!data || data.length === 0) return;
      const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
      const px = e.clientX - rect.left;
      const clamped = Math.min(width - padding.right, Math.max(padding.left, px));
      const t = (clamped - padding.left) / innerW;
      const idx = Math.round(t * Math.max(0, data.length - 1));
      const d = data[idx];
      const norm = (Number(d.total) - min) / range;
      const y = padding.top + (1 - norm) * innerH;
      setHover({ idx, x: padding.left + (innerW * idx) / Math.max(1, data.length - 1), y });
    },
    [data, innerW, innerH, min, range, padding.left, padding.right, padding.top]
  );

  const handleLeave = useCallback(() => setHover(null), []);

  const latest = data && data.length > 0 ? data[data.length - 1] : undefined;

  return (
    <div style={{ gridColumn: `span ${span}`, background: '#6b7280', color: '#fff', borderRadius: 12, padding: 16 }}>
      <div style={{ fontSize: 20, fontWeight: 700, marginBottom: 8, textAlign: 'left' }}>資産推移 (30件)</div>
      {data && data.length > 0 ? (
        <div style={{ width: '100%', overflowX: 'auto', position: 'relative' }}>
          <svg
            ref={svgRef}
            viewBox={`0 0 ${width} ${height}`}
            width="100%"
            height={height}
            role="img"
            aria-label="資産推移"
            onMouseMove={handleMove}
            onMouseLeave={handleLeave}
          >
            <rect x={0} y={0} width={width} height={height} fill="#6b7280" />

            {/* Axes */}
            <g stroke="#d1d5db" strokeWidth={1}>
              {/* Y axis line */}
              <line x1={padding.left} y1={padding.top} x2={padding.left} y2={height - padding.bottom} />
              {/* X axis line */}
              <line x1={padding.left} y1={height - padding.bottom} x2={width - padding.right} y2={height - padding.bottom} />
              {/* Y ticks and labels */}
              {yTicks.map((t, i) => (
                <g key={`y-${i}`}>
                  <line x1={padding.left - 6} x2={width - padding.right} y1={t.y} y2={t.y} strokeOpacity={0.2} />
                  <text x={padding.left - 8} y={t.y} fill="#fff" fontSize={10} textAnchor="end" dominantBaseline="middle">
                    {formatJPY(Math.round(t.value))}
                  </text>
                </g>
              ))}
              {/* X ticks and labels */}
              {xTicks.map((t, i) => (
                <g key={`x-${i}`}>
                  <line x1={t.x} x2={t.x} y1={height - padding.bottom} y2={height - padding.bottom + 6} />
                  <text x={t.x} y={height - padding.bottom + 18} fill="#fff" fontSize={10} textAnchor="middle">
                    {t.label}
                  </text>
                </g>
              ))}
            </g>

            {/* Line */}
            <polyline fill="none" stroke="#ffffff" strokeWidth={2} points={points} />

            {/* Hover cursor */}
            {hover && (
              <g>
                <line x1={hover.x} x2={hover.x} y1={padding.top} y2={height - padding.bottom} stroke="#fef08a" strokeWidth={1} />
                <circle cx={hover.x} cy={hover.y} r={3} fill="#fef08a" />
              </g>
            )}
          </svg>

          {/* Tooltip */}
          {hover && (
            <div
              style={{
                position: 'absolute',
                left: `${(hover.x / width) * 100}%`,
                top: 8,
                transform: 'translateX(-50%)',
                background: 'rgba(0,0,0,0.6)',
                color: '#fff',
                padding: '6px 8px',
                borderRadius: 6,
                fontSize: 12,
                whiteSpace: 'nowrap',
                pointerEvents: 'none',
              }}
            >
              <div>{formatDate(data[hover.idx].date)}</div>
              <div><strong>{formatJPY(Number(data[hover.idx].total))}</strong></div>
            </div>
          )}

          {latest && (
            <div style={{ marginTop: 8, fontSize: 14 }}>
              最新 {formatDate(latest.date)}: <strong>{formatJPY(latest.total)}</strong>
            </div>
          )}
        </div>
      ) : (
        <div style={{ fontSize: '1rem' }}>{empty || 'データなし'}</div>
      )}
    </div>
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
