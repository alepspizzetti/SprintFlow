import { useEffect, useRef, useState, type CSSProperties } from 'react'

type Sprint = { id: string; name: string; startDate: string; endDate: string; status: string }
type Segment = { date: string; hours: number }
type Activity = { memberId: string | null; memberName: string | null; kind: 'Assigned' | 'Active' | 'Paused' | 'Review' | 'Done'; startedAt: string; endedAt: string | null }
type Blockage = { taskId: number; blockingTaskId: number | null; date: string; hours: number; cause: string }
type Task = {
  id: number; storyId: number; storyTitle: string; title: string; stage: string; state: string
  remainingWork: number; order: number | null; segments: Segment[]; exceedsSprint: boolean
  azureUrl: string | null; activity: Activity[]
}
type Person = { id: string; name: string; role: string; dailyCapacityHours: number; tasks: Task[]; blockages: Blockage[]; daysOff: string[] }
type Timeline = {
  sprint: Sprint; today: string; dates: string[]; teamDaysOff: string[]
  stories: Array<{ id: number; title: string; order: number; completedTasks: number; totalTasks: number; isDone: boolean; state: string; description: string | null; azureUrl: string | null }>
  people: Person[]
  summary: { completedStories: number; totalStories: number; forecastEnd: string | null }
  ignoredTaskCount: number
  syncedAt: string | null
}
type Row = { task: Task; owner: Person }
type StoryGroup = { id: number; title: string; position: number; state: string; isDone: boolean; description: string | null; azureUrl: string | null; rows: Row[] }
type Bar = { task: Task; owner: Person; kind: string; start: number; end: number; forecastStart: number | null; pauses: number[][]; blockages: Blockage[]; lane: number }
type Selection = { task: Task; owner: Person; blockage?: Blockage } | null

const formatarDataCurta = (date: string) => date.slice(8, 10) + '/' + date.slice(5, 7)
const obterDataLocal = (timestamp: string) => {
  const value = new Date(timestamp)
  return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0')
}
const formatarDataHora = (timestamp: string) => new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(timestamp))
const openedAt = Date.now()
const formatarUltimaSincronizacao = (timestamp: string) => obterDataLocal(timestamp) === obterDataLocal(new Date().toISOString())
  ? 'Atualizado às ' + new Intl.DateTimeFormat('pt-BR', { timeStyle: 'short' }).format(new Date(timestamp))
  : 'Atualizado em ' + formatarDataHora(timestamp)
const formatarDia = (date: string) => formatarDataCurta(date) + ' ' +
  new Intl.DateTimeFormat('pt-BR', { weekday: 'short' }).format(new Date(date + 'T12:00:00')).replace('.', '').toUpperCase()
const formatarAtividade = (period: Activity, earlier: Activity[]) => {
  if (period.kind === 'Active')
    return earlier.some(item => item.kind === 'Active' && item.memberId === period.memberId) ? 'Retomou' : 'Iniciou'
  return { Assigned: 'Recebeu', Paused: 'Parou', Review: 'Entrou em revisão', Done: 'Concluiu' }[period.kind]
}
const obterIndiceDoDia = (date: string, dates: string[]) => {
  const index = dates.findIndex(item => item >= date)
  return index < 0 ? dates.length : index
}
const calcularPosicao = (timestamp: string, dates: string[]) => {
  const value = new Date(timestamp)
  const date = obterDataLocal(timestamp)
  const index = obterIndiceDoDia(date, dates)
  if (dates[index] !== date) return index
  return index + (value.getHours() + value.getMinutes() / 60) / 24
}
const kindByState: Record<string, string> = { Done: 'done', 'In Progress': 'active', Review: 'review' }
const ownerColors = ['#1f6391', '#7b2d63', '#2f7a4f', '#8a5a1c', '#5b3f9a', '#1d7a7a', '#8f3b3b', '#4a6a1f']
const obterCorDoResponsavel = (id: string) => ownerColors[[...id].reduce((hash, char) => hash + char.charCodeAt(0), 0) % ownerColors.length]
const obterCorDoStatus = (state: string) => ({
  done: '#2f7a4f',
  'await release': '#7b2d63',
  'awaiting release': '#7b2d63',
  testing: '#d8b64c',
  todo: '#596475',
  'to do': '#596475'
} as Record<string, string>)[state.toLowerCase()] ?? '#596475'
const calcularBarraDaTarefa = (task: Task, owner: Person, dates: string[], now: number): Omit<Bar, 'lane'> => {
  const minimum = 0.3
  const last = task.activity.at(-1)
  const inicio = task.activity.find(period => period.kind === 'Active') ?? task.activity.find(period => period.kind === 'Review' || period.kind === 'Done')
  const actualStart = inicio ? calcularPosicao(inicio.startedAt, dates) : null
  const actualEnd = last ? last.kind === 'Done' ? calcularPosicao(last.startedAt, dates) : now : null
  const first = task.segments[0]
  const final = task.segments.at(-1)
  const forecastStart = first ? Math.max(obterIndiceDoDia(first.date, dates), now) : null
  const forecastEnd = final ? obterIndiceDoDia(final.date, dates) + Math.min(final.hours / (owner.dailyCapacityHours || final.hours), 1) : null
  const start = Math.min(actualStart ?? forecastStart ?? now, dates.length - minimum)
  const end = Math.min(Math.max(forecastEnd ?? actualEnd ?? start, start + minimum), dates.length)
  return {
    task, owner, start, end,
    kind: last?.kind === 'Paused' && task.state !== 'Done' ? 'paused' : kindByState[task.state] ?? (first ? 'forecast' : 'idle'),
    forecastStart: actualStart !== null && forecastStart !== null ? forecastStart : null,
    pauses: task.activity.filter(period => period.kind === 'Paused')
      .map(period => [calcularPosicao(period.startedAt, dates), period.endedAt ? calcularPosicao(period.endedAt, dates) : now]),
    blockages: owner.blockages.filter(item => item.taskId === task.id)
  }
}
const montarBarras = (rows: Row[], dates: string[], now: number): Bar[] => {
  const laneEnds: number[] = []
  return rows.map(({ task, owner }) => {
    const bar = calcularBarraDaTarefa(task, owner, dates, now)
    let lane = laneEnds.findIndex(end => end <= bar.start)
    if (lane < 0) lane = laneEnds.length
    laneEnds[lane] = bar.end
    return { ...bar, lane }
  })
}
const collapseKey = 'sprintflow.collapseDone'
const laneHeight = 44
const agruparHistorias = (timeline: Timeline): StoryGroup[] => {
  const stories = new Map<number, StoryGroup>(timeline.stories.map((story, index) =>
    [story.id, { id: story.id, title: story.title, position: index + 1, state: story.state, isDone: story.isDone, description: story.description, azureUrl: story.azureUrl, rows: [] }]))
  for (const owner of timeline.people) for (const task of owner.tasks)
    stories.get(task.storyId)?.rows.push({ task, owner })
  for (const story of stories.values())
    story.rows.sort((a, b) => (a.task.order ?? Number.MAX_VALUE) - (b.task.order ?? Number.MAX_VALUE) || a.task.id - b.task.id)
  return [...stories.values()]
}

function Aplicacao() {
  const [sprints, definirSprints] = useState<Sprint[]>([])
  const [team, definirTime] = useState('')
  const [sprintId, definirIdDaSprint] = useState('')
  const [timelines, definirTimelines] = useState<Record<string, Timeline>>({})
  const [loading, definirCarregamento] = useState(false)
  const [syncing, definirSincronizacao] = useState(false)
  const [error, definirErro] = useState('')
  const [selection, definirSelecao] = useState<Selection>(null)
  const [selectedStory, definirHistoriaSelecionada] = useState<StoryGroup | null>(null)
  const cliqueDaHistoria = useRef<ReturnType<typeof setTimeout> | null>(null)
  const autoSynced = useRef(new Set<string>())
  const rolagemDaTimeline = useRef<HTMLDivElement>(null)
  const sprintCentralizada = useRef('')
  const [collapseDone, definirRecolhimentoDasConcluidas] = useState(() => localStorage.getItem(collapseKey) !== 'false')
  const [toggled, definirHistoriasAlternadas] = useState(new Set<number>())
  const timeline = timelines[sprintId]

  useEffect(() => {
    definirHistoriaSelecionada(null)
    return () => { if (cliqueDaHistoria.current) clearTimeout(cliqueDaHistoria.current) }
  }, [sprintId])

  useEffect(() => {
    fetch('/api/settings')
      .then(response => response.ok ? response.json() as Promise<{ team: string }> : null)
      .then(settings => definirTime(settings?.team ?? ''))
      .catch(() => definirTime(''))
  }, [])

  useEffect(() => {
    fetch('/api/sprints')
      .then(async response => {
        if (!response.ok) throw new Error('Configure o time em appsettings.Local.json e verifique o Azure CLI.')
        return response.json() as Promise<Sprint[]>
      })
      .then(items => {
        definirSprints(items)
        definirIdDaSprint(items.find(item => item.status === 'Current')?.id ?? items[0]?.id ?? '')
      })
      .catch(cause => definirErro(cause.message))
  }, [])

  useEffect(() => {
    if (!sprintId || timelines[sprintId]) return
    const controller = new AbortController()
    definirCarregamento(true)
    definirErro('')
    fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/timeline', { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Não foi possível carregar a sprint. Verifique a conexão com o Azure DevOps.')
        return response.json() as Promise<Timeline>
      })
      .then(data => definirTimelines(previous => ({ ...previous, [sprintId]: data })))
      .catch(cause => { if (cause.name !== 'AbortError') definirErro(cause.message) })
      .finally(() => { if (!controller.signal.aborted) definirCarregamento(false) })
    return () => controller.abort()
  }, [sprintId, timelines])

  const sincronizar = async () => {
    if (!sprintId || syncing) return
    definirSincronizacao(true)
    definirErro('')
    try {
      const response = await fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/sync', { method: 'POST' })
      if (!response.ok) throw new Error('Sync falhou. Os dados anteriores continuam na tela.')
      const data = await response.json() as Timeline
      definirTimelines(previous => ({ ...previous, [sprintId]: data }))
    } catch (cause) {
      definirErro(cause instanceof Error ? cause.message : 'Sync falhou.')
    } finally {
      definirSincronizacao(false)
    }
  }

  useEffect(() => {
    if (!timeline || syncing || autoSynced.current.has(sprintId)) return
    autoSynced.current.add(sprintId)
    if (timeline.syncedAt && new Date(timeline.syncedAt).getTime() >= openedAt) return
    sincronizar()
  }, [sprintId, timeline, syncing])

  const stories = timeline ? agruparHistorias(timeline) : []
  const completedStories = stories.filter(story => story.isDone).length
  const taskCount = timeline?.people.reduce((count, person) => count + person.tasks.length, 0) ?? 0
  const activeCount = timeline?.people.reduce((count, person) => count + person.tasks.filter(task => task.state === 'In Progress').length, 0) ?? 0
  const blockageCount = timeline?.people.reduce((count, person) => count + person.blockages.length, 0) ?? 0
  const days = timeline?.dates.length ?? 0
  const teamDaysOff = timeline?.teamDaysOff ?? []
  const now = timeline ? calcularPosicao(new Date().toISOString(), timeline.dates) : 0
  const showNow = timeline?.sprint.status === 'Current' && now > 0 && now < days
  useEffect(() => {
    const rolagem = rolagemDaTimeline.current
    if (!timeline || !rolagem || sprintCentralizada.current === sprintId) return
    const larguraDoDia = rolagem.querySelector('.days > div')?.getBoundingClientRect().width ?? 0
    rolagem.scrollLeft = showNow ? Math.max(0, now * larguraDoDia - (rolagem.clientWidth - 320) / 2) : 0
    sprintCentralizada.current = sprintId
  }, [sprintId, timeline])

  const calcularPercentual = (value: number) => value / days * 100 + '%'
  const alterarRecolhimentoDasConcluidas = (value: boolean) => {
    localStorage.setItem(collapseKey, String(value))
    definirRecolhimentoDasConcluidas(value)
    definirHistoriasAlternadas(new Set())
  }
  const alternarHistoria = (id: number) => definirHistoriasAlternadas(previous => {
    const next = new Set(previous)
    if (!next.delete(id)) next.add(id)
    return next
  })

  const clicarNaHistoria = (story: StoryGroup, detail: number) => {
    if (cliqueDaHistoria.current) clearTimeout(cliqueDaHistoria.current)
    if (detail > 1) return
    if (detail === 0) definirHistoriaSelecionada(story)
    else cliqueDaHistoria.current = setTimeout(() => definirHistoriaSelecionada(story), 350)
  }
  const alternarTodasAsHistorias = () => {
    const elegiveis = stories.filter(story => !collapseDone || ['todo', 'to do', 'in progress', 'testing', 'testings'].includes(story.state.toLowerCase()))
    const recolher = elegiveis.some(story => !((collapseDone && story.isDone) !== toggled.has(story.id)))
    definirHistoriasAlternadas(new Set(stories.filter(story => {
      const collapsed = recolher || !elegiveis.includes(story)
      return collapsed !== (collapseDone && story.isDone)
    }).map(story => story.id)))
  }

  return <div className="app">
    <header className="topbar">
      <div className="brand">SPRINT<span>FLOW</span><small>{team.toUpperCase()}</small></div>
      <div className="toolbar">
        <label htmlFor="sprint">Sprint</label>
        <select id="sprint" value={sprintId} onChange={event => { definirIdDaSprint(event.target.value); definirSelecao(null); definirErro('') }}>
          {!sprints.length && <option value="">Carregando sprints…</option>}
          {sprints.map(sprint => <option key={sprint.id} value={sprint.id}>{sprint.name} · {formatarDataCurta(sprint.startDate)}–{formatarDataCurta(sprint.endDate)}</option>)}
        </select>
        <details className="settings">
          <summary aria-label="Configurações">⚙</summary>
          <label><input type="checkbox" checked={collapseDone} onChange={event => alterarRecolhimentoDasConcluidas(event.target.checked)} />Recolher USs concluídas</label>
        </details>
      </div>
    </header>

    <main>
      {error && <div className="message error" role="alert">{error}</div>}
      {loading && !timeline && <div className="message">Carregando dados e histórico do Azure DevOps…</div>}
      {!loading && !timeline && !error && <div className="message">Selecione uma sprint para ver o trabalho da squad.</div>}
      {timeline && <>
        <div className="metrics">
          <div><span>USs concluídas</span><strong>{completedStories}<small> / {stories.length}</small></strong></div>
          <div><span>Em andamento</span><strong>{activeCount}</strong></div>
          <div><span>Dias de bloqueio</span><strong className={blockageCount ? 'alert' : ''}>{blockageCount}</strong></div>
          <div><span>Fim previsto</span><strong>{timeline.summary.forecastEnd ? formatarDataCurta(timeline.summary.forecastEnd) : '—'}</strong></div>
          {timeline.ignoredTaskCount > 0 && <div><span>Sem previsão</span><strong>{timeline.ignoredTaskCount}</strong></div>}
        </div>
        <section className="board" style={{ '--days': days } as CSSProperties}>
          <div className="board-bar">
            <div><b>TIMELINE POR US</b><span>ORDEM DO AZURE · HOJE {formatarDataCurta(timeline.today)}</span></div>
            <div><span>{stories.length} USs · {taskCount} tasks</span>
              <button className="sync" onClick={sincronizar} disabled={!sprintId || syncing}>{syncing ? 'SINCRONIZANDO…' : 'SYNC SPRINT'}</button>
              {timeline.syncedAt && <span className="synced">{formatarUltimaSincronizacao(timeline.syncedAt)}</span>}</div>
          </div>
          <div className="board-scroll" ref={rolagemDaTimeline}>
            <div className="board-grid" style={{ minWidth: 320 + days * 110 }}>
              <div className="board-head"><div><button className="story-toggle" onDoubleClick={alternarTodasAsHistorias} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); alternarTodasAsHistorias() } }} title="Duplo clique para expandir ou recolher as USs conforme a configuração">USER STORY</button></div>
                <div className="days">{timeline.dates.map(date => <div key={date} className={(date === timeline.today ? 'today' : '') + (teamDaysOff.includes(date) ? ' team-day-off-day' : '')} title={teamDaysOff.includes(date) ? 'Folga do time em ' + formatarDataCurta(date) : undefined}>{formatarDia(date)}</div>)}</div>
              </div>
              {stories.map(story => {
                const done = story.isDone
                const collapsed = (collapseDone && done) !== toggled.has(story.id)
                const bars = collapsed ? [] : montarBarras(story.rows, timeline.dates, now)
                const lanes = Math.max(0, ...bars.map(bar => bar.lane + 1))
                return <div className={'story' + (collapsed ? ' collapsed' : '')} key={story.id}>
                  <button className="story-label" onClick={event => clicarNaHistoria(story, event.detail)} onDoubleClick={() => { if (cliqueDaHistoria.current) clearTimeout(cliqueDaHistoria.current); definirHistoriaSelecionada(null); alternarHistoria(story.id) }} title={story.title + ' · Clique para detalhes; duplo clique para expandir ou recolher'}>
                    <span className="story-title"><b>{collapsed ? '▸' : '▾'} #{story.position}</b>{story.title}</span>
                    <small>US {story.id} · <span className="owner story-status" data-state={story.state.toLowerCase()} style={{ background: obterCorDoStatus(story.state) }}>{story.state}</span></small>
                  </button>
                  <div className="lanes" style={{ height: collapsed ? undefined : lanes * laneHeight + 10 }}>
                    {timeline.dates.map((date, index) => teamDaysOff.includes(date) && <i key={date} className="team-day-off" style={{ left: calcularPercentual(index), width: calcularPercentual(1) }} />)}
                    {showNow && <i className="now" style={{ left: calcularPercentual(now) }} />}
                    {bars.map(bar => {
                      const calcularPosicaoNaBarra = (value: number) => (Math.min(Math.max(value, bar.start), bar.end) - bar.start) / (bar.end - bar.start) * 100 + '%'
                      const folgasDaBarra = timeline.dates.filter((date, index) => bar.owner.daysOff?.includes(date) && !teamDaysOff.includes(date) && index < bar.end && index + 1 > bar.start)
                      return <button key={bar.task.id} className={'bar bar-' + bar.kind}
                        style={{ left: calcularPercentual(bar.start), width: calcularPercentual(bar.end - bar.start), top: 5 + bar.lane * laneHeight }}
                        title={'#' + bar.task.id + ' ' + bar.task.title + ' · ' + bar.owner.name + ' · ' + bar.task.state + ' · ' + bar.task.remainingWork + 'h' + (folgasDaBarra.length ? ' · Folga individual: ' + folgasDaBarra.map(formatarDataCurta).join(', ') : '')}
                        onClick={() => definirSelecao({ task: bar.task, owner: bar.owner, blockage: bar.blockages[0] })}>
                        {bar.pauses.map(([from, to], index) => <i key={index} className="pause" style={{ left: calcularPosicaoNaBarra(from), width: 'calc(' + calcularPosicaoNaBarra(to) + ' - ' + calcularPosicaoNaBarra(from) + ')' }} />)}
                        {bar.forecastStart !== null && bar.forecastStart < bar.end && <i className="forecast" style={{ left: calcularPosicaoNaBarra(bar.forecastStart), right: 0 }} />}
                        {folgasDaBarra.map(date => <i key={date} className="individual-day-off" style={{ left: calcularPosicaoNaBarra(obterIndiceDoDia(date, timeline.dates)), width: 'calc(' + calcularPosicaoNaBarra(obterIndiceDoDia(date, timeline.dates) + 1) + ' - ' + calcularPosicaoNaBarra(obterIndiceDoDia(date, timeline.dates)) + ')' }} />)}
                        {bar.blockages.map(item => <i key={item.date} className="blocked" style={{ left: calcularPosicaoNaBarra(obterIndiceDoDia(item.date, timeline.dates)), width: 'calc(' + calcularPosicaoNaBarra(obterIndiceDoDia(item.date, timeline.dates) + 1) + ' - ' + calcularPosicaoNaBarra(obterIndiceDoDia(item.date, timeline.dates)) + ')' }} />)}
                        <span className="bar-text">#{bar.task.id} {bar.task.title}</span>
                        <span className="owner" style={{ background: obterCorDoResponsavel(bar.owner.id) }}>{bar.owner.name}</span>
                      </button>
                    })}
                  </div>
                </div>
              })}
            </div>
          </div>
        </section>
        <div className="legend"><span><i className="bar-done" />Concluída</span><span><i className="bar-active" />Em andamento</span>
          <span><i className="bar-review" />Em revisão</span><span><i className="bar-paused" />Parada</span>
          <span><i className="bar-forecast" />Prevista</span><span><i className="legend-blocked" />Bloqueio previsto</span>
          <span><i className="legend-team-day-off" />Folga do time</span><span><i className="individual-day-off" />Folga individual</span>
          <span>Clique na barra para ver datas e horas exatas · clique na US para detalhes · duplo clique na US ou em USER STORY para expandir ou recolher.</span></div>
      </>}
    </main>

    {selectedStory && <div className="modal-backdrop" onMouseDown={() => definirHistoriaSelecionada(null)}>
      <section className="detail" role="dialog" aria-modal="true" aria-labelledby="story-detail-title" onMouseDown={event => event.stopPropagation()}>
        <button className="close" aria-label="Fechar" onClick={() => definirHistoriaSelecionada(null)}>×</button>
        <p className="eyebrow">US #{selectedStory.id}</p>
        <h2 id="story-detail-title">{selectedStory.title}</h2>
        <p>Estado: {selectedStory.state} · Tasks concluídas: {selectedStory.rows.filter(row => row.task.state === 'Done').length}/{selectedStory.rows.length} · Restante: {selectedStory.rows.reduce((hours, row) => hours + row.task.remainingWork, 0)}h</p>
        <h3>Descrição</h3>
        {selectedStory.description ? <iframe className="story-description" title="Descrição completa da US" sandbox="" srcDoc={`<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: data:; style-src 'unsafe-inline'"><style>body{font:14px Arial,sans-serif;color:#c1d0de;line-height:1.5;overflow-wrap:anywhere}img{max-width:100%}a{color:#60eaff}</style>${selectedStory.description}`} /> : <p>Descrição não disponível. Sincronize a sprint caso os dados estejam desatualizados.</p>}
        <h3>Tasks vinculadas</h3>
        {selectedStory.rows.length ? <ul className="story-tasks">{selectedStory.rows.map(({ task, owner }) => <li key={task.id}>
          <button onClick={() => { definirHistoriaSelecionada(null); definirSelecao({ task, owner, blockage: owner.blockages.find(item => item.taskId === task.id) }) }}>#{task.id} {task.title}</button>
          <p>{owner.name} · {task.state} · Restante: {task.remainingWork}h{task.exceedsSprint ? ' · Excede a sprint' : ''}</p>
        </li>)}</ul> : <p>Sem tasks vinculadas nesta sprint.</p>}
        {selectedStory.azureUrl && <a href={selectedStory.azureUrl} target="_blank" rel="noreferrer">Abrir no Azure DevOps ↗</a>}
      </section>
    </div>}

    {selection && <div className="modal-backdrop" onMouseDown={() => definirSelecao(null)}>
      <section className="detail" role="dialog" aria-modal="true" aria-labelledby="detail-title" onMouseDown={event => event.stopPropagation()}>
        <button className="close" aria-label="Fechar" onClick={() => definirSelecao(null)}>×</button>
        <p className="eyebrow">US #{selection.task.storyId} · TASK #{selection.task.id}</p>
        <h2 id="detail-title">{selection.task.title}</h2>
        <p>Responsável atual: {selection.owner.name} · Estado: {selection.task.state} · Restante: {selection.task.remainingWork}h</p>
        {selection.blockage && <div className="blockage-detail"><b>Bloqueio previsto em {formatarDataCurta(selection.blockage.date)}</b><p>{selection.blockage.cause}</p>
          {selection.blockage.blockingTaskId && <p>Aguardando task #{selection.blockage.blockingTaskId}</p>}</div>}
        <h3>Histórico no Azure DevOps</h3>
        {selection.task.activity.length ? <ol className="events">{selection.task.activity.map((period, index) =>
          <li key={period.startedAt + index} className={'event-' + period.kind.toLowerCase()}>
            <strong>{formatarAtividade(period, selection.task.activity.slice(0, index))}</strong>
            <span>{period.memberName ?? 'Sem responsável'} · {formatarDataHora(period.startedAt)}</span>
            {period.endedAt && period.kind !== 'Assigned' && period.kind !== 'Done' && <small>até {formatarDataHora(period.endedAt)}</small>}
          </li>)}</ol> : <p>Sem revisões de estado disponíveis para esta sprint.</p>}
        {selection.task.azureUrl && <a href={selection.task.azureUrl} target="_blank" rel="noreferrer">Abrir no Azure DevOps ↗</a>}
      </section>
    </div>}
  </div>
}

export default Aplicacao
