// @ts-check

/** @typedef {{ id: string, name: string, startDate: string, endDate: string, status: string }} Sprint */
/** @typedef {{ date: string, hours: number, start: string, end: string }} Segment */
/** @typedef {{ memberId: string | null, memberName: string | null, kind: 'Assigned' | 'Active' | 'Paused' | 'Review' | 'Done', startedAt: string, endedAt: string | null }} Activity */
/** @typedef {{ taskId: number, blockingTaskId: number | null, date: string, hours: number, cause: string }} Blockage */
/**
 * @typedef {{
 *   id: number, storyId: number, storyTitle: string, title: string, stage: string, state: string,
 *   remainingWork: number, order: number | null, segments: Segment[], exceedsSprint: boolean,
 *   azureUrl: string | null, activity: Activity[]
 * }} Task
 */
/** @typedef {{ id: string, name: string, role: string, dailyCapacityHours: number, tasks: Task[], blockages: Blockage[], daysOff: string[] }} Person */
/** @typedef {{ id: number, title: string, order: number, completedTasks: number, totalTasks: number, isDone: boolean, state: string, description: string | null, azureUrl: string | null }} Story */
/**
 * @typedef {{
 *   sprint: Sprint, today: string, dates: string[], teamDaysOff: string[],
 *   stories: Story[], people: Person[],
 *   summary: { completedStories: number, totalStories: number, forecastEnd: string | null },
 *   ignoredTaskCount: number, syncedAt: string | null
 * }} Timeline
 */
/** @typedef {{ task: Task, owner: Person }} Row */
/** @typedef {{ id: number, title: string, position: number, state: string, isDone: boolean, description: string | null, azureUrl: string | null, rows: Row[] }} StoryGroup */
/** @typedef {{ task: Task, owner: Person, kind: string, start: number, end: number, forecastStart: number | null, pauses: number[][], blockages: Blockage[] }} BarraSemFaixa */
/** @typedef {BarraSemFaixa & { lane: number }} Bar */
/** @typedef {{ task: Task, owner: Person, blockage?: Blockage } | null} Selecao */
/**
 * @typedef {{
 *   timeline: Timeline, stories: StoryGroup[], completedStories: number, taskCount: number, activeCount: number,
 *   blockageCount: number, days: number, teamDaysOff: string[], now: number, showNow: boolean
 * }} Vista
 */
/** @typedef {string | number | Node | null | undefined | false} Filho */

/** @param {string} date */
const formatarDataCurta = date => date.slice(8, 10) + '/' + date.slice(5, 7)
/** @param {string} timestamp */
const obterDataLocal = timestamp => {
  const value = new Date(timestamp)
  return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0')
}
/** @param {string} timestamp */
const formatarDataHora = timestamp => new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(timestamp))
const openedAt = Date.now()
/** @param {string} timestamp */
const formatarUltimaSincronizacao = timestamp => obterDataLocal(timestamp) === obterDataLocal(new Date().toISOString())
  ? 'Atualizado às ' + new Intl.DateTimeFormat('pt-BR', { timeStyle: 'short' }).format(new Date(timestamp))
  : 'Atualizado em ' + formatarDataHora(timestamp)
/** @param {string} date */
const formatarDia = date => formatarDataCurta(date) + ' ' +
  new Intl.DateTimeFormat('pt-BR', { weekday: 'short' }).format(new Date(date + 'T12:00:00')).replace('.', '').toUpperCase()
/** @param {Activity} period @param {Activity[]} earlier */
const formatarAtividade = (period, earlier) => {
  if (period.kind === 'Active')
    return earlier.some(item => item.kind === 'Active' && item.memberId === period.memberId) ? 'Retomou' : 'Iniciou'
  return { Assigned: 'Recebeu', Paused: 'Parou', Review: 'Entrou em revisão', Done: 'Concluiu' }[period.kind]
}
/** @param {string} date @param {string[]} dates */
const obterIndiceDoDia = (date, dates) => {
  const index = dates.findIndex(item => item >= date)
  return index < 0 ? dates.length : index
}
/** @param {string} timestamp @param {string[]} dates */
const calcularPosicao = (timestamp, dates) => {
  const value = new Date(timestamp)
  const date = obterDataLocal(timestamp)
  const index = obterIndiceDoDia(date, dates)
  if (dates[index] !== date) return index
  return index + (value.getHours() + value.getMinutes() / 60) / 24
}
/** @type {Record<string, string>} */
const kindByState = { Done: 'done', 'In Progress': 'active', Review: 'review' }
const ownerColors = ['#1f6391', '#7b2d63', '#2f7a4f', '#8a5a1c', '#5b3f9a', '#1d7a7a', '#8f3b3b', '#4a6a1f']
/** @param {string} id */
const obterCorDoResponsavel = id => ownerColors[[...id].reduce((hash, char) => hash + char.charCodeAt(0), 0) % ownerColors.length]
/** @type {Record<string, string>} */
const coresDoStatus = {
  done: '#2f7a4f',
  'await release': '#7b2d63',
  'awaiting release': '#7b2d63',
  testing: '#d8b64c',
  'in progress': '#1f6391',
  todo: '#596475',
  'to do': '#596475'
}
/** @param {string} state */
const obterCorDoStatus = state => coresDoStatus[state.toLowerCase()] ?? '#596475'

/** @param {Task} task @param {Person} owner @param {string[]} dates @param {number} now @returns {BarraSemFaixa} */
const calcularBarraDaTarefa = (task, owner, dates, now) => {
  const minimum = 0.3
  const last = task.activity.at(-1)
  const inicio = task.activity.find(period => period.kind === 'Active') ?? task.activity.find(period => period.kind === 'Review' || period.kind === 'Done')
  const actualStart = inicio ? calcularPosicao(inicio.startedAt, dates) : null
  const actualEnd = last ? last.kind === 'Done' ? calcularPosicao(last.startedAt, dates) : now : null
  const first = task.segments[0]
  const final = task.segments.at(-1)
  const forecastStart = first ? Math.max(calcularPosicao(first.start, dates), now) : null
  const forecastEnd = final ? calcularPosicao(final.end, dates) : null
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
/** @param {Row[]} rows @param {string[]} dates @param {number} now @returns {Bar[]} */
const montarBarras = (rows, dates, now) => {
  /** @type {number[]} */
  const laneEnds = []
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
/** @param {Timeline} timeline @returns {StoryGroup[]} */
const agruparHistorias = timeline => {
  /** @type {Map<number, StoryGroup>} */
  const stories = new Map(timeline.stories.map((story, index) =>
    [story.id, { id: story.id, title: story.title, position: index + 1, state: story.state, isDone: story.isDone, description: story.description, azureUrl: story.azureUrl, rows: [] }]))
  for (const owner of timeline.people) for (const task of owner.tasks)
    stories.get(task.storyId)?.rows.push({ task, owner })
  for (const story of stories.values())
    story.rows.sort((a, b) => (a.task.order ?? Number.MAX_VALUE) - (b.task.order ?? Number.MAX_VALUE) || a.task.id - b.task.id)
  return [...stories.values()]
}

/**
 * @param {string} tag
 * @param {Record<string, any> | null} propriedades
 * @param {...(Filho | Filho[])} filhos
 * @returns {HTMLElement}
 */
const elemento = (tag, propriedades, ...filhos) => {
  const novo = document.createElement(tag)
  for (const filho of filhos.flat()) if (filho !== null && filho !== undefined && filho !== false)
    novo.append(filho instanceof Node ? filho : String(filho))
  for (const [chave, valor] of Object.entries(propriedades ?? {})) aplicarPropriedade(novo, chave, valor)
  return novo
}
/** @param {HTMLElement} alvo @param {string} chave @param {any} valor */
const aplicarPropriedade = (alvo, chave, valor) => {
  if (valor === undefined || valor === null) return
  if (chave === 'style') aplicarEstilo(alvo, valor)
  else if (chave.startsWith('on')) alvo.addEventListener(chave.slice(2), valor)
  else if (chave in alvo) /** @type {any} */ (alvo)[chave] = valor
  else alvo.setAttribute(chave, String(valor))
}
/** @param {HTMLElement} alvo @param {Record<string, string | undefined>} estilo */
const aplicarEstilo = (alvo, estilo) => {
  for (const [chave, valor] of Object.entries(estilo)) {
    if (valor === undefined) continue
    if (chave.startsWith('--')) alvo.style.setProperty(chave, valor)
    else /** @type {any} */ (alvo.style)[chave] = valor
  }
}

/**
 * @type {{
 *   sprints: Sprint[], team: string, sprintId: string, timelines: Record<string, Timeline>, loading: boolean, syncing: boolean,
 *   error: string, selection: Selecao, selectedStory: StoryGroup | null, collapseDone: boolean, toggled: Set<number>
 * }}
 */
const estado = {
  sprints: [],
  team: '',
  sprintId: '',
  timelines: {},
  loading: false,
  syncing: false,
  error: '',
  selection: null,
  selectedStory: null,
  collapseDone: localStorage.getItem(collapseKey) !== 'false',
  toggled: new Set()
}
/** @type {ReturnType<typeof setTimeout> | null} */
let cliqueDaHistoria = null
/** @type {AbortController | null} */
let controleDoCarregamento = null
const autoSynced = new Set()
let sprintCentralizada = ''
/** @type {{ story: StoryGroup | null, selection: Selecao, nos: HTMLElement[] }} */
let modaisRenderizados = { story: null, selection: null, nos: [] }
/** @type {Sprint[] | null} */
let sprintsRenderizadas = null

/** @param {Partial<typeof estado>} parcial */
const definirEstado = parcial => {
  Object.assign(estado, parcial)
  renderizar()
}
const limparCliqueDaHistoria = () => {
  if (cliqueDaHistoria) clearTimeout(cliqueDaHistoria)
}

const carregarConfiguracoes = () => {
  fetch('/api/settings')
    .then(response => response.ok ? /** @type {Promise<{ team: string }>} */ (response.json()) : null)
    .then(settings => definirEstado({ team: settings?.team ?? '' }))
    .catch(() => definirEstado({ team: '' }))
}
const carregarSprints = () => {
  fetch('/api/sprints')
    .then(async response => {
      if (!response.ok) throw new Error('Configure o time em appsettings.Local.json e verifique o Azure CLI.')
      return /** @type {Promise<Sprint[]>} */ (response.json())
    })
    .then(items => {
      definirEstado({ sprints: items })
      definirIdDaSprint(items.find(item => item.status === 'Current')?.id ?? items[0]?.id ?? '')
    })
    .catch(cause => definirEstado({ error: cause.message }))
}
/** @param {string} sprintId */
const definirIdDaSprint = sprintId => {
  limparCliqueDaHistoria()
  definirEstado({ sprintId, selectedStory: null })
  carregarTimeline()
}
/** @param {string} sprintId */
const selecionarSprint = sprintId => {
  estado.selection = null
  estado.error = ''
  definirIdDaSprint(sprintId)
}
const carregarTimeline = () => {
  const sprintId = estado.sprintId
  controleDoCarregamento?.abort()
  controleDoCarregamento = null
  if (!sprintId || estado.timelines[sprintId]) return
  const controller = new AbortController()
  controleDoCarregamento = controller
  definirEstado({ loading: true, error: '' })
  fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/timeline', { signal: controller.signal })
    .then(async response => {
      if (!response.ok) throw new Error('Não foi possível carregar a sprint. Verifique a conexão com o Azure DevOps.')
      return /** @type {Promise<Timeline>} */ (response.json())
    })
    .then(data => definirEstado({ timelines: { ...estado.timelines, [sprintId]: data } }))
    .catch(cause => { if (cause.name !== 'AbortError') definirEstado({ error: cause.message }) })
    .finally(() => { if (!controller.signal.aborted) definirEstado({ loading: false }) })
}
const sincronizar = async () => {
  const sprintId = estado.sprintId
  if (!sprintId || estado.syncing) return
  definirEstado({ syncing: true, error: '' })
  try {
    const response = await fetch('/api/sprints/' + encodeURIComponent(sprintId) + '/sync', { method: 'POST' })
    if (!response.ok) throw new Error('Sync falhou. Os dados anteriores continuam na tela.')
    const data = /** @type {Timeline} */ (await response.json())
    estado.timelines = { ...estado.timelines, [sprintId]: data }
  } catch (cause) {
    estado.error = cause instanceof Error ? cause.message : 'Sync falhou.'
  } finally {
    definirEstado({ syncing: false })
  }
}
const verificarSincronizacaoAutomatica = () => {
  const timeline = estado.timelines[estado.sprintId]
  if (!timeline || estado.syncing || autoSynced.has(estado.sprintId)) return
  autoSynced.add(estado.sprintId)
  if (timeline.syncedAt && new Date(timeline.syncedAt).getTime() >= openedAt) return
  sincronizar()
}

/** @param {boolean} value */
const alterarRecolhimentoDasConcluidas = value => {
  localStorage.setItem(collapseKey, String(value))
  definirEstado({ collapseDone: value, toggled: new Set() })
}
/** @param {number} id */
const alternarHistoria = id => {
  const next = new Set(estado.toggled)
  if (!next.delete(id)) next.add(id)
  return next
}
/** @param {StoryGroup} story @param {number} detail */
const clicarNaHistoria = (story, detail) => {
  limparCliqueDaHistoria()
  if (detail > 1) return
  if (detail === 0) definirEstado({ selectedStory: story })
  else cliqueDaHistoria = setTimeout(() => definirEstado({ selectedStory: story }), 350)
}
/** @param {StoryGroup} story */
const clicarDuasVezesNaHistoria = story => {
  limparCliqueDaHistoria()
  definirEstado({ selectedStory: null, toggled: alternarHistoria(story.id) })
}
const alternarTodasAsHistorias = () => {
  const timeline = estado.timelines[estado.sprintId]
  const stories = timeline ? agruparHistorias(timeline) : []
  const { collapseDone, toggled } = estado
  const elegiveis = stories.filter(story => !collapseDone || ['todo', 'to do', 'in progress', 'testing', 'testings'].includes(story.state.toLowerCase()))
  const recolher = elegiveis.some(story => !((collapseDone && story.isDone) !== toggled.has(story.id)))
  definirEstado({
    toggled: new Set(stories.filter(story => {
      const collapsed = recolher || !elegiveis.includes(story)
      return collapsed !== (collapseDone && story.isDone)
    }).map(story => story.id))
  })
}

/** @returns {Vista | null} */
const montarVista = () => {
  const timeline = estado.timelines[estado.sprintId]
  if (!timeline) return null
  const stories = agruparHistorias(timeline)
  const days = timeline.dates.length
  const now = calcularPosicao(new Date().toISOString(), timeline.dates)
  return {
    timeline, stories, days, now,
    completedStories: stories.filter(story => story.isDone).length,
    taskCount: timeline.people.reduce((count, person) => count + person.tasks.length, 0),
    activeCount: timeline.people.reduce((count, person) => count + person.tasks.filter(task => task.state === 'In Progress').length, 0),
    blockageCount: timeline.people.reduce((count, person) => count + person.blockages.length, 0),
    teamDaysOff: timeline.teamDaysOff ?? [],
    showNow: timeline.sprint.status === 'Current' && now > 0 && now < days
  }
}
/** @param {number} value @param {number} days */
const calcularPercentual = (value, days) => value / days * 100 + '%'

const montarCabecalho = () => elemento('header', { className: 'topbar' },
  elemento('div', { className: 'brand' }, 'SPRINT', elemento('span', null, 'FLOW'), elemento('small', { id: 'nome-do-time' })),
  elemento('div', { className: 'toolbar' },
    elemento('label', { htmlFor: 'sprint' }, 'Sprint'),
    elemento('select', { id: 'sprint', onchange: (/** @type {Event} */ event) => selecionarSprint(/** @type {HTMLSelectElement} */ (event.target).value) }),
    elemento('details', { className: 'settings' },
      elemento('summary', { 'aria-label': 'Configurações' }, '⚙'),
      elemento('label', null,
        elemento('input', { type: 'checkbox', id: 'recolher-concluidas', onchange: (/** @type {Event} */ event) => alterarRecolhimentoDasConcluidas(/** @type {HTMLInputElement} */ (event.target).checked) }),
        'Recolher USs concluídas'))))

const atualizarCabecalho = () => {
  const nomeDoTime = /** @type {HTMLElement} */ (document.getElementById('nome-do-time'))
  const select = /** @type {HTMLSelectElement} */ (document.getElementById('sprint'))
  const recolher = /** @type {HTMLInputElement} */ (document.getElementById('recolher-concluidas'))
  nomeDoTime.textContent = estado.team.toUpperCase()
  if (sprintsRenderizadas !== estado.sprints) select.replaceChildren(...montarOpcoesDeSprint())
  sprintsRenderizadas = estado.sprints
  if (select.value !== estado.sprintId) select.value = estado.sprintId
  recolher.checked = estado.collapseDone
}
const montarOpcoesDeSprint = () => [
  !estado.sprints.length && elemento('option', { value: '' }, 'Carregando sprints…'),
  ...estado.sprints.map(sprint => elemento('option', { value: sprint.id }, sprint.name, ' · ', formatarDataCurta(sprint.startDate), '–', formatarDataCurta(sprint.endDate)))
].filter(opcao => opcao !== false)

/** @param {Vista | null} vista */
const montarConteudo = vista => [
  estado.error && elemento('div', { className: 'message error', role: 'alert' }, estado.error),
  estado.loading && !vista && elemento('div', { className: 'message' }, 'Carregando dados e histórico do Azure DevOps…'),
  !estado.loading && !vista && !estado.error && elemento('div', { className: 'message' }, 'Selecione uma sprint para ver o trabalho da squad.'),
  ...(vista ? [montarMetricas(vista), montarQuadro(vista), montarLegenda()] : [])
]

/** @param {Vista} vista */
const montarMetricas = vista => elemento('div', { className: 'metrics' },
  elemento('div', null, elemento('span', null, 'USs concluídas'), elemento('strong', null, vista.completedStories, elemento('small', null, ' / ', vista.stories.length))),
  elemento('div', null, elemento('span', null, 'Em andamento'), elemento('strong', null, vista.activeCount)),
  elemento('div', null, elemento('span', null, 'Dias de bloqueio'), elemento('strong', { className: vista.blockageCount ? 'alert' : '' }, vista.blockageCount)),
  elemento('div', null, elemento('span', null, 'Fim previsto'), elemento('strong', null, vista.timeline.summary.forecastEnd ? formatarDataCurta(vista.timeline.summary.forecastEnd) : '—')),
  vista.timeline.ignoredTaskCount > 0 && elemento('div', null, elemento('span', null, 'Sem previsão'), elemento('strong', null, vista.timeline.ignoredTaskCount)))

/** @param {Vista} vista */
const montarQuadro = vista => elemento('section', { className: 'board', style: { '--days': String(vista.days) } },
  montarBarraDoQuadro(vista),
  elemento('div', { className: 'board-scroll' },
    elemento('div', { className: 'board-grid', style: { minWidth: 320 + vista.days * 110 + 'px' } },
      montarCabecalhoDaGrade(vista),
      vista.stories.map(story => montarHistoria(story, vista)))))

/** @param {Vista} vista */
const montarBarraDoQuadro = ({ timeline, stories, taskCount }) => elemento('div', { className: 'board-bar' },
  elemento('div', null, elemento('b', null, 'TIMELINE POR US'), elemento('span', null, 'ORDEM DO AZURE · HOJE ', formatarDataCurta(timeline.today))),
  elemento('div', null,
    elemento('span', null, stories.length, ' USs · ', taskCount, ' tasks'),
    elemento('button', { className: 'sync', 'data-chave': 'sync', onclick: sincronizar, disabled: !estado.sprintId || estado.syncing }, estado.syncing ? 'SINCRONIZANDO…' : 'SYNC SPRINT'),
    timeline.syncedAt && elemento('span', { className: 'synced' }, formatarUltimaSincronizacao(timeline.syncedAt))))

/** @param {KeyboardEvent} event */
const teclarNoAlternadorDeHistorias = event => {
  if (event.key !== 'Enter' && event.key !== ' ') return
  event.preventDefault()
  alternarTodasAsHistorias()
}
/** @param {Vista} vista */
const montarCabecalhoDaGrade = ({ timeline, teamDaysOff }) => elemento('div', { className: 'board-head' },
  elemento('div', null, elemento('button', {
    className: 'story-toggle', 'data-chave': 'story-toggle', ondblclick: alternarTodasAsHistorias, onkeydown: teclarNoAlternadorDeHistorias,
    title: 'Duplo clique para expandir ou recolher as USs conforme a configuração'
  }, 'USER STORY')),
  elemento('div', { className: 'days' }, timeline.dates.map(date => elemento('div', {
    className: (date === timeline.today ? 'today' : '') + (teamDaysOff.includes(date) ? ' team-day-off-day' : ''),
    title: teamDaysOff.includes(date) ? 'Folga do time em ' + formatarDataCurta(date) : undefined
  }, formatarDia(date)))))

/** @param {StoryGroup} story @param {Vista} vista */
const montarHistoria = (story, vista) => {
  const collapsed = (estado.collapseDone && story.isDone) !== estado.toggled.has(story.id)
  return elemento('div', { className: 'story' + (collapsed ? ' collapsed' : '') },
    elemento('button', {
      className: 'story-label', 'data-chave': 'historia-' + story.id,
      onclick: (/** @type {MouseEvent} */ event) => clicarNaHistoria(story, event.detail),
      ondblclick: () => clicarDuasVezesNaHistoria(story),
      title: story.title + ' · Clique para detalhes; duplo clique para expandir ou recolher'
    },
    elemento('span', { className: 'story-title' }, elemento('b', null, collapsed ? '▸' : '▾', ' #', story.position), story.title),
    elemento('small', null, 'US ', story.id, ' · ', elemento('span', { className: 'owner story-status', 'data-state': story.state.toLowerCase(), style: { background: obterCorDoStatus(story.state) } }, story.state))),
    montarFaixas(story, collapsed, vista))
}
/** @param {StoryGroup} story @param {boolean} collapsed @param {Vista} vista */
const montarFaixas = (story, collapsed, vista) => {
  const { timeline, teamDaysOff, days, now, showNow } = vista
  const bars = collapsed ? [] : montarBarras(story.rows, timeline.dates, now)
  const lanes = Math.max(0, ...bars.map(bar => bar.lane + 1))
  return elemento('div', { className: 'lanes', style: { height: collapsed ? undefined : lanes * laneHeight + 10 + 'px' } },
    timeline.dates.map((date, index) => teamDaysOff.includes(date) && elemento('i', { className: 'team-day-off', style: { left: calcularPercentual(index, days), width: calcularPercentual(1, days) } })),
    showNow && elemento('i', { className: 'now', style: { left: calcularPercentual(now, days) } }),
    bars.map(bar => montarBarra(bar, vista)))
}
/** @param {Bar} bar @param {Vista} vista */
const montarBarra = (bar, vista) => {
  const { timeline, teamDaysOff, days } = vista
  const calcularPosicaoNaBarra = (/** @type {number} */ value) => (Math.min(Math.max(value, bar.start), bar.end) - bar.start) / (bar.end - bar.start) * 100 + '%'
  const calcularTrecho = (/** @type {number} */ from, /** @type {number} */ to) => ({ left: calcularPosicaoNaBarra(from), width: 'calc(' + calcularPosicaoNaBarra(to) + ' - ' + calcularPosicaoNaBarra(from) + ')' })
  const calcularTrechoDoDia = (/** @type {string} */ date) => calcularTrecho(obterIndiceDoDia(date, timeline.dates), obterIndiceDoDia(date, timeline.dates) + 1)
  const folgasDaBarra = timeline.dates.filter((date, index) => bar.owner.daysOff?.includes(date) && !teamDaysOff.includes(date) && index < bar.end && index + 1 > bar.start)
  return elemento('button', {
    className: 'bar bar-' + bar.kind, 'data-chave': 'barra-' + bar.task.id,
    style: { left: calcularPercentual(bar.start, days), width: calcularPercentual(bar.end - bar.start, days), top: 5 + bar.lane * laneHeight + 'px' },
    title: '#' + bar.task.id + ' ' + bar.task.title + ' · ' + bar.owner.name + ' · ' + bar.task.state + ' · ' + bar.task.remainingWork + 'h' + (folgasDaBarra.length ? ' · Folga individual: ' + folgasDaBarra.map(formatarDataCurta).join(', ') : ''),
    onclick: () => definirEstado({ selection: { task: bar.task, owner: bar.owner, blockage: bar.blockages[0] } })
  },
  bar.pauses.map(([from, to]) => elemento('i', { className: 'pause', style: calcularTrecho(from, to) })),
  bar.forecastStart !== null && bar.forecastStart < bar.end && elemento('i', { className: 'forecast', style: { left: calcularPosicaoNaBarra(bar.forecastStart), right: '0px' } }),
  folgasDaBarra.map(date => elemento('i', { className: 'individual-day-off', style: calcularTrechoDoDia(date) })),
  bar.blockages.map(item => elemento('i', { className: 'blocked', style: calcularTrechoDoDia(item.date) })),
  elemento('span', { className: 'bar-text' }, '#', bar.task.id, ' ', bar.task.title),
  elemento('span', { className: 'owner', style: { background: obterCorDoResponsavel(bar.owner.id) } }, bar.owner.name))
}

/** @param {string} className @param {string} texto */
const montarItemDaLegenda = (className, texto) => elemento('span', null, elemento('i', { className }), texto)
const montarLegenda = () => elemento('div', { className: 'legend' },
  montarItemDaLegenda('bar-done', 'Concluída'), montarItemDaLegenda('bar-active', 'Em andamento'),
  montarItemDaLegenda('bar-review', 'Em revisão'), montarItemDaLegenda('bar-paused', 'Parada'),
  montarItemDaLegenda('bar-forecast', 'Prevista'), montarItemDaLegenda('legend-blocked', 'Bloqueio previsto'),
  montarItemDaLegenda('legend-team-day-off', 'Folga do time'), montarItemDaLegenda('individual-day-off', 'Folga individual'),
  elemento('span', null, 'Clique na barra para ver datas e horas exatas · clique na US para detalhes · duplo clique na US ou em USER STORY para expandir ou recolher.'))

/** @param {() => void} fechar @param {string} idDoTitulo @param {...(Filho | Filho[])} conteudo */
const montarModal = (fechar, idDoTitulo, ...conteudo) => elemento('div', { className: 'modal-backdrop', onmousedown: fechar },
  elemento('section', { className: 'detail', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': idDoTitulo, onmousedown: (/** @type {Event} */ event) => event.stopPropagation() },
    elemento('button', { className: 'close', 'aria-label': 'Fechar', onclick: fechar }, '×'),
    ...conteudo))

/** @param {StoryGroup} selectedStory */
const montarDetalheDaHistoria = selectedStory => montarModal(() => definirEstado({ selectedStory: null }), 'story-detail-title',
  elemento('p', { className: 'eyebrow' }, 'US #', selectedStory.id),
  elemento('h2', { id: 'story-detail-title' }, selectedStory.title),
  elemento('p', null, 'Estado: ', selectedStory.state, ' · Tasks concluídas: ', selectedStory.rows.filter(row => row.task.state === 'Done').length, '/', selectedStory.rows.length, ' · Restante: ', selectedStory.rows.reduce((hours, row) => hours + row.task.remainingWork, 0), 'h'),
  elemento('h3', null, 'Descrição'),
  montarDescricaoDaHistoria(selectedStory.description),
  elemento('h3', null, 'Tasks vinculadas'),
  selectedStory.rows.length ? elemento('ul', { className: 'story-tasks' }, selectedStory.rows.map(montarTarefaDaHistoria)) : elemento('p', null, 'Sem tasks vinculadas nesta sprint.'),
  selectedStory.azureUrl && elemento('a', { href: selectedStory.azureUrl, target: '_blank', rel: 'noreferrer' }, 'Abrir no Azure DevOps ↗'))

/** @param {string | null} description */
const montarDescricaoDaHistoria = description => description
  ? elemento('iframe', {
    className: 'story-description', title: 'Descrição completa da US', sandbox: '',
    srcdoc: `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: data:; style-src 'unsafe-inline'"><style>body{font:14px Arial,sans-serif;color:#c1d0de;line-height:1.5;overflow-wrap:anywhere}img{max-width:100%}a{color:#60eaff}</style>${description}`
  })
  : elemento('p', null, 'Descrição não disponível. Sincronize a sprint caso os dados estejam desatualizados.')

/** @param {Row} row */
const montarTarefaDaHistoria = ({ task, owner }) => elemento('li', null,
  elemento('button', { onclick: () => definirEstado({ selectedStory: null, selection: { task, owner, blockage: owner.blockages.find(item => item.taskId === task.id) } }) }, '#', task.id, ' ', task.title),
  elemento('p', null, owner.name, ' · ', task.state, ' · Restante: ', task.remainingWork, 'h', task.exceedsSprint ? ' · Excede a sprint' : ''))

/** @param {NonNullable<Selecao>} selection */
const montarDetalheDaTarefa = selection => montarModal(() => definirEstado({ selection: null }), 'detail-title',
  elemento('p', { className: 'eyebrow' }, 'US #', selection.task.storyId, ' · TASK #', selection.task.id),
  elemento('h2', { id: 'detail-title' }, selection.task.title),
  elemento('p', null, 'Responsável atual: ', selection.owner.name, ' · Estado: ', selection.task.state, ' · Restante: ', selection.task.remainingWork, 'h'),
  selection.blockage && montarDetalheDoBloqueio(selection.blockage),
  elemento('h3', null, 'Histórico no Azure DevOps'),
  selection.task.activity.length ? elemento('ol', { className: 'events' }, selection.task.activity.map(montarEvento)) : elemento('p', null, 'Sem revisões de estado disponíveis para esta sprint.'),
  selection.task.azureUrl && elemento('a', { href: selection.task.azureUrl, target: '_blank', rel: 'noreferrer' }, 'Abrir no Azure DevOps ↗'))

/** @param {Blockage} blockage */
const montarDetalheDoBloqueio = blockage => elemento('div', { className: 'blockage-detail' },
  elemento('b', null, 'Bloqueio previsto em ', formatarDataCurta(blockage.date)),
  elemento('p', null, blockage.cause),
  blockage.blockingTaskId && elemento('p', null, 'Aguardando task #', blockage.blockingTaskId))

/** @param {Activity} period @param {number} index @param {Activity[]} activity */
const montarEvento = (period, index, activity) => elemento('li', { className: 'event-' + period.kind.toLowerCase() },
  elemento('strong', null, formatarAtividade(period, activity.slice(0, index))),
  elemento('span', null, period.memberName ?? 'Sem responsável', ' · ', formatarDataHora(period.startedAt)),
  period.endedAt && period.kind !== 'Assigned' && period.kind !== 'Done' && elemento('small', null, 'até ', formatarDataHora(period.endedAt)))

const raiz = /** @type {HTMLElement} */ (document.getElementById('root'))
const principal = elemento('main', null)
const aplicacao = elemento('div', { className: 'app' }, montarCabecalho(), principal)

const renderizar = () => {
  const rolagemAnterior = principal.querySelector('.board-scroll')
  const posicao = rolagemAnterior ? [rolagemAnterior.scrollLeft, rolagemAnterior.scrollTop] : null
  const chaveEmFoco = document.activeElement instanceof HTMLElement && principal.contains(document.activeElement) ? document.activeElement.dataset.chave : undefined
  const vista = montarVista()
  atualizarCabecalho()
  principal.replaceChildren(...montarConteudo(vista).filter(filho => filho instanceof Node))
  restaurarRolagem(posicao)
  restaurarFoco(chaveEmFoco)
  renderizarModais()
  centralizarTimeline(vista)
  verificarSincronizacaoAutomatica()
}
/** @param {number[] | null} posicao */
const restaurarRolagem = posicao => {
  const rolagem = principal.querySelector('.board-scroll')
  if (!rolagem || !posicao) return
  rolagem.scrollLeft = posicao[0]
  rolagem.scrollTop = posicao[1]
}
/** @param {string | undefined} chave */
const restaurarFoco = chave => {
  if (!chave) return
  const alvo = principal.querySelector('[data-chave="' + chave + '"]')
  if (alvo instanceof HTMLElement) alvo.focus({ preventScroll: true })
}
const renderizarModais = () => {
  if (modaisRenderizados.story === estado.selectedStory && modaisRenderizados.selection === estado.selection) return
  for (const no of modaisRenderizados.nos) no.remove()
  const nos = [estado.selectedStory && montarDetalheDaHistoria(estado.selectedStory), estado.selection && montarDetalheDaTarefa(estado.selection)]
    .filter(no => no instanceof HTMLElement)
  aplicacao.append(...nos)
  modaisRenderizados = { story: estado.selectedStory, selection: estado.selection, nos }
}
/** @param {Vista | null} vista */
const centralizarTimeline = vista => {
  const rolagem = principal.querySelector('.board-scroll')
  if (!vista || !rolagem || sprintCentralizada === estado.sprintId) return
  const larguraDoDia = rolagem.querySelector('.days > div')?.getBoundingClientRect().width ?? 0
  rolagem.scrollLeft = vista.showNow ? Math.max(0, vista.now * larguraDoDia - (rolagem.clientWidth - 320) / 2) : 0
  sprintCentralizada = estado.sprintId
}

raiz.replaceChildren(aplicacao)
renderizar()
carregarConfiguracoes()
carregarSprints()
