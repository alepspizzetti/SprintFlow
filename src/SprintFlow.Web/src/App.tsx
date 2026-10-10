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
type Person = { id: string; name: string; role: string; dailyCapacityHours: number; tasks: Task[]; blockages: Blockage[] }
type Timeline = {
  sprint: Sprint; today: string; dates: string[]
  stories: Array<{ id: number; title: string; order: number; completedTasks: number; totalTasks: number }>
  people: Person[]
  summary: { completedStories: number; totalStories: number; forecastEnd: string | null }
  ignoredTaskCount: number
  syncedAt: string | null
}
type Row = { task: Task; owner: Person }
type StoryGroup = { id: number; title: string; position: number; completedTasks: number; totalTasks: number; rows: Row[] }
type Bar = { task: Task; owner: Person; kind: string; start: number; end: number; forecastStart: number | null; pauses: number[][]; blockages: Blockage[]; lane: number }
type Selection = { task: Task; owner: Person; blockage?: Blockage } | null

const shortDate = (date: string) => date.slice(8, 10) + '/' + date.slice(5, 7)
const localDate = (timestamp: string) => {
  const value = new Date(timestamp)
  return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0')
}
const stamp = (timestamp: string) => new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(timestamp))
const openedAt = Date.now()
const syncedLabel = (timestamp: string) => localDate(timestamp) === localDate(new Date().toISOString())
  ? 'Atualizado às ' + new Intl.DateTimeFormat('pt-BR', { timeStyle: 'short' }).format(new Date(timestamp))
  : 'Atualizado em ' + stamp(timestamp)
const dayLabel = (date: string) => shortDate(date) + ' ' +
  new Intl.DateTimeFormat('pt-BR', { weekday: 'short' }).format(new Date(date + 'T12:00:00')).replace('.', '').toUpperCase()
const activityLabel = (period: Activity, earlier: Activity[]) => {
  if (period.kind === 'Active')
    return earlier.some(item => item.kind === 'Active' && item.memberId === period.memberId) ? 'Retomou' : 'Iniciou'
  return { Assigned: 'Recebeu', Paused: 'Parou', Review: 'Entrou em revisão', Done: 'Concluiu' }[period.kind]
}
const dayIndex = (date: string, dates: string[]) => {
  const index = dates.findIndex(item => item >= date)
  return index < 0 ? dates.length : index
}
const position = (timestamp: string, dates: string[]) => {
  const value = new Date(timestamp)
  const date = localDate(timestamp)
  const index = dayIndex(date, dates)
  if (dates[index] !== date) return index
  return index + (value.getHours() + value.getMinutes() / 60) / 24
}
const kindByState: Record<string, string> = { Done: 'done', 'In Progress': 'active', Review: 'review' }
const ownerColors = ['#1f6391', '#7b2d63', '#2f7a4f', '#8a5a1c', '#5b3f9a', '#1d7a7a', '#8f3b3b', '#4a6a1f']
const ownerColor = (id: string) => ownerColors[[...id].reduce((hash, char) => hash + char.charCodeAt(0), 0) % ownerColors.length]
const measureTask = (task: Task, owner: Person, dates: string[], now: number): Omit<Bar, 'lane'> => {
  const minimum = 0.3
  const last = task.activity.at(-1)
  const actualStart = task.activity.length ? position(task.activity[0].startedAt, dates) : null
  const actualEnd = last ? last.kind === 'Done' ? position(last.startedAt, dates) : now : null
  const first = task.segments[0]
  const final = task.segments.at(-1)
  const forecastStart = first ? Math.max(dayIndex(first.date, dates), now) : null
  const forecastEnd = final ? dayIndex(final.date, dates) + Math.min(final.hours / (owner.dailyCapacityHours || final.hours), 1) : null
  const start = Math.min(actualStart ?? forecastStart ?? now, dates.length - minimum)
  const end = Math.min(Math.max(forecastEnd ?? actualEnd ?? start, start + minimum), dates.length)
  return {
    task, owner, start, end,
    kind: last?.kind === 'Paused' && task.state !== 'Done' ? 'paused' : kindByState[task.state] ?? (first ? 'planned' : 'idle'),
    forecastStart: actualStart !== null && forecastStart !== null ? forecastStart : null,
    pauses: task.activity.filter(period => period.kind === 'Paused')
      .map(period => [position(period.startedAt, dates), period.endedAt ? position(period.endedAt, dates) : now]),
    blockages: owner.blockages.filter(item => item.taskId === task.id)
  }
}
const buildBars = (rows: Row[], dates: string[], now: number): Bar[] => {
  const laneEnds: number[] = []
  return rows.map(({ task, owner }) => {
    const bar = measureTask(task, owner, dates, now)
    let lane = laneEnds.findIndex(end => end <= bar.start)
    if (lane < 0) lane = laneEnds.length
    laneEnds[lane] = bar.end
    return { ...bar, lane }
  })
}
const collapseKey = 'sprintflow.collapseDone'
const laneHeight = 44
const buildStories = (timeline: Timeline): StoryGroup[] => {
  const stories = new Map<number, StoryGroup>(timeline.stories.map((story, index) =>
    [story.id, { id: story.id, title: story.title, position: index + 1, completedTasks: story.completedTasks, totalTasks: story.totalTasks, rows: [] }]))
  for (const owner of timeline.people) for (const task of owner.tasks)
    stories.get(task.storyId)?.rows.push({ task, owner })
  for (const story of stories.values())
    story.rows.sort((a, b) => (a.task.order ?? Number.MAX_VALUE) - (b.task.order ?? Number.MAX_VALUE) || a.task.id - b.task.id)
  return [...stories.values()]
}

function App() {
  const [sprints, setSprints] = useState<Sprint[]>([])
  const [team, setTeam] = useState('')
  const [sprintId, setSprintId] = useState('')
  const [timelines, setTimelines] = useState<Record<string, Timeline>>({})
  const [loading, setLoading] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const [error, setError] = useState('')
  const [selection, setSelection] = useState<Selection>(null)
  const autoSynced = useRef(new Set<string>())
  const [collapseDone, setCollapseDone] = useState(() => localStorage.getItem(collapseKey) !== 'false')
  const [toggled, setToggled] = useState(new Set<number>())
  const timeline = timelines[sprintId]

  useEffect(() => {
    fetch('/api/settings')
      .then(response => response.ok ? response.json() as Promise<{ team: string }> : null)
      .then(settings => setTeam(settings?.team ?? ''))
      .catch(() => setTeam(''))
  }, [])

  useEffect(() => {
    fetch('/api/sprints')
      .then(async response => {
        if (!response.ok) throw new Error('Configure o time em appsettings.Local.json e verifique o Azure CLI.')
        return response.json() as Promise<Sprint[]>
      })
      .then(items => {
        setSprints(items)
        setSprintId(items.find(item => item.status === 'Current')?.id ?? items[0]?.id ?? '')
      })
      .catch(cause => setError(cause.message))
  }, [])

  useEffect(() => {
    if (!sprintId || timelines[sprintId]) return
    const controller = new AbortController()
    setLoading(true)
    setError('')
    fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/timeline', { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Não foi possível carregar a sprint. Verifique a conexão com o Azure DevOps.')
        return response.json() as Promise<Timeline>
      })
      .then(data => setTimelines(previous => ({ ...previous, [sprintId]: data })))
      .catch(cause => { if (cause.name !== 'AbortError') setError(cause.message) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [sprintId, timelines])

  const sync = async () => {
    if (!sprintId || syncing) return
    setSyncing(true)
    setError('')
    try {
      const response = await fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/sync', { method: 'POST' })
      if (!response.ok) throw new Error('Sync falhou. Os dados anteriores continuam na tela.')
      const data = await response.json() as Timeline
      setTimelines(previous => ({ ...previous, [sprintId]: data }))
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Sync falhou.')
    } finally {
      setSyncing(false)
    }
  }

  useEffect(() => {
    if (!timeline || syncing || autoSynced.current.has(sprintId)) return
    autoSynced.current.add(sprintId)
    if (timeline.syncedAt && new Date(timeline.syncedAt).getTime() >= openedAt) return
    sync()
  }, [sprintId, timeline, syncing])

  const stories = timeline ? buildStories(timeline) : []
  const completedStories = stories.filter(story => story.totalTasks > 0 && story.completedTasks === story.totalTasks).length
  const taskCount = timeline?.people.reduce((count, person) => count + person.tasks.length, 0) ?? 0
  const activeCount = timeline?.people.reduce((count, person) => count + person.tasks.filter(task => task.state === 'In Progress').length, 0) ?? 0
  const blockageCount = timeline?.people.reduce((count, person) => count + person.blockages.length, 0) ?? 0
  const days = timeline?.dates.length ?? 0
  const now = timeline ? position(new Date().toISOString(), timeline.dates) : 0
  const showNow = timeline?.sprint.status === 'Current' && now > 0 && now < days
  const pct = (value: number) => value / days * 100 + '%'
  const changeCollapseDone = (value: boolean) => {
    localStorage.setItem(collapseKey, String(value))
    setCollapseDone(value)
    setToggled(new Set())
  }
  const toggleStory = (id: number) => setToggled(previous => {
    const next = new Set(previous)
    if (!next.delete(id)) next.add(id)
    return next
  })

  return <div className="app">
    <header className="topbar">
      <div className="brand">SPRINT<span>FLOW</span><small>{team.toUpperCase()}</small></div>
      <div className="toolbar">
        <label htmlFor="sprint">Sprint</label>
        <select id="sprint" value={sprintId} onChange={event => { setSprintId(event.target.value); setSelection(null); setError('') }}>
          {!sprints.length && <option value="">Carregando sprints…</option>}
          {sprints.map(sprint => <option key={sprint.id} value={sprint.id}>{sprint.name} · {shortDate(sprint.startDate)}–{shortDate(sprint.endDate)}</option>)}
        </select>
        <details className="settings">
          <summary aria-label="Configurações">⚙</summary>
          <label><input type="checkbox" checked={collapseDone} onChange={event => changeCollapseDone(event.target.checked)} />Recolher USs concluídas automaticamente</label>
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
          <div><span>Fim previsto</span><strong>{timeline.summary.forecastEnd ? shortDate(timeline.summary.forecastEnd) : '—'}</strong></div>
          {timeline.ignoredTaskCount > 0 && <div><span>Sem previsão</span><strong>{timeline.ignoredTaskCount}</strong></div>}
        </div>
        <section className="board" style={{ '--days': days } as CSSProperties}>
          <div className="board-bar">
            <div><b>TIMELINE POR US</b><span>ORDEM DO AZURE · HOJE {shortDate(timeline.today)}</span></div>
            <div><span>{stories.length} USs · {taskCount} tasks</span>
              <button className="sync" onClick={sync} disabled={!sprintId || syncing}>{syncing ? 'SINCRONIZANDO…' : 'SYNC SPRINT'}</button>
              {timeline.syncedAt && <span className="synced">{syncedLabel(timeline.syncedAt)}</span>}</div>
          </div>
          <div className="board-scroll">
            <div className="board-grid" style={{ minWidth: 320 + days * 110 }}>
              <div className="board-head"><div>USER STORY</div>
                <div className="days">{timeline.dates.map(date => <div key={date} className={date === timeline.today ? 'today' : ''}>{dayLabel(date)}</div>)}</div></div>
              {stories.map(story => {
                const done = story.totalTasks > 0 && story.completedTasks === story.totalTasks
                const collapsed = (collapseDone && done) !== toggled.has(story.id)
                const bars = collapsed ? [] : buildBars(story.rows, timeline.dates, now)
                const lanes = Math.max(0, ...bars.map(bar => bar.lane + 1))
                return <div className={'story' + (collapsed ? ' collapsed' : '')} key={story.id}>
                  <button className="story-label" onClick={() => toggleStory(story.id)} title={story.title}>
                    <span className="story-title"><b>{collapsed ? '▸' : '▾'} #{story.position}</b>{story.title}</span>
                    <small>US {story.id} · {story.completedTasks}/{story.totalTasks} concluídas</small>
                  </button>
                  <div className="lanes" style={{ height: collapsed ? undefined : lanes * laneHeight + 10 }}>
                    {showNow && <i className="now" style={{ left: pct(now) }} />}
                    {bars.map(bar => {
                      const inside = (value: number) => (Math.min(Math.max(value, bar.start), bar.end) - bar.start) / (bar.end - bar.start) * 100 + '%'
                      return <button key={bar.task.id} className={'bar bar-' + bar.kind}
                        style={{ left: pct(bar.start), width: pct(bar.end - bar.start), top: 5 + bar.lane * laneHeight }}
                        title={'#' + bar.task.id + ' ' + bar.task.title + ' · ' + bar.owner.name + ' · ' + bar.task.state + ' · ' + bar.task.remainingWork + 'h'}
                        onClick={() => setSelection({ task: bar.task, owner: bar.owner, blockage: bar.blockages[0] })}>
                        {bar.pauses.map(([from, to], index) => <i key={index} className="pause" style={{ left: inside(from), width: 'calc(' + inside(to) + ' - ' + inside(from) + ')' }} />)}
                        {bar.forecastStart !== null && bar.forecastStart < bar.end && <i className="forecast" style={{ left: inside(bar.forecastStart), right: 0 }} />}
                        {bar.blockages.map(item => <i key={item.date} className="blocked" style={{ left: inside(dayIndex(item.date, timeline.dates)), width: 'calc(' + inside(dayIndex(item.date, timeline.dates) + 1) + ' - ' + inside(dayIndex(item.date, timeline.dates)) + ')' }} />)}
                        <span className="bar-text">#{bar.task.id} {bar.task.title}</span>
                        <span className="owner" style={{ background: ownerColor(bar.owner.id) }}>{bar.owner.name}</span>
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
          <span><i className="bar-planned" />Prevista</span><span><i className="legend-blocked" />Bloqueio previsto</span>
          <span>Clique na barra para ver datas e horas exatas · clique na US para expandir ou recolher.</span></div>
      </>}
    </main>

    {selection && <div className="modal-backdrop" onMouseDown={() => setSelection(null)}>
      <section className="detail" role="dialog" aria-modal="true" aria-labelledby="detail-title" onMouseDown={event => event.stopPropagation()}>
        <button className="close" aria-label="Fechar" onClick={() => setSelection(null)}>×</button>
        <p className="eyebrow">US #{selection.task.storyId} · TASK #{selection.task.id}</p>
        <h2 id="detail-title">{selection.task.title}</h2>
        <p>Responsável atual: {selection.owner.name} · Estado: {selection.task.state} · Restante: {selection.task.remainingWork}h</p>
        {selection.blockage && <div className="blockage-detail"><b>Bloqueio previsto em {shortDate(selection.blockage.date)}</b><p>{selection.blockage.cause}</p>
          {selection.blockage.blockingTaskId && <p>Aguardando task #{selection.blockage.blockingTaskId}</p>}</div>}
        <h3>Histórico no Azure DevOps</h3>
        {selection.task.activity.length ? <ol className="events">{selection.task.activity.map((period, index) =>
          <li key={period.startedAt + index} className={'event-' + period.kind.toLowerCase()}>
            <strong>{activityLabel(period, selection.task.activity.slice(0, index))}</strong>
            <span>{period.memberName ?? 'Sem responsável'} · {stamp(period.startedAt)}</span>
            {period.endedAt && period.kind !== 'Assigned' && period.kind !== 'Done' && <small>até {stamp(period.endedAt)}</small>}
          </li>)}</ol> : <p>Sem revisões de estado disponíveis para esta sprint.</p>}
        {selection.task.azureUrl && <a href={selection.task.azureUrl} target="_blank" rel="noreferrer">Abrir no Azure DevOps ↗</a>}
      </section>
    </div>}
  </div>
}

export default App
