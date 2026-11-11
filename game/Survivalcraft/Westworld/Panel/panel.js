'use strict';

const $ = id => document.getElementById(id);
const compact = matchMedia('(max-width: 760px)');
const labels = {
  MoveTo: '移动', FaceTowards: '转向', PickUp: '拾取', Eat: '进食', Wait: '等待', Speak: '发言',
  OfferItem: '提出交接', RespondToOffer: '回应交接', idle: '空闲', waiting: '等待执行', running: '进行中',
  blocked: '受阻', succeeded: '成功', failed: '失败', cancelled: '取消', skipped: '跳过',
  blockedPath: '路径受阻', timedOut: '超时', targetChanged: '目标变化', outOfReach: '超出范围',
  insufficientItems: '物品不足', inventoryFull: '背包已满', preconditionsNotMet: '前置条件不满足',
  actionFailed: '动作失败', insufficientEffect: '实际效果未达到要求',
  ready: '候选已返回，等待交接', reserved: '等待账本确认', received: '已回包，待处理', inFlight: '请求在途',
  hungry: '饥饿', peckish: '有点饿', fed: '饱足', whisper: '轻声', normal: '正常', shout: '大声',
  interrupted: '中断', speaking: '发言中', completed: '已完成', unknown: '用量未知',
  offered: '提出交接', declined: '拒绝交接', transferred: '实际转移'
};
const dispatchLabels = {
  running: '调度运行中', budgetLimited: '预算不足，停止新派发', draining: '停止新派发，结清在途请求',
  userPaused: '模拟已暂停', faulted: '调度故障'
};
const sections = [['overview','概况'],['task','任务'],['speech','对话'],['memory','记忆'],['history','记录']];
let snapshot = null;
let selectedId = null;
let view = 'people';
let detail = 'overview';
let eventFocus = null;
let eventTask = null;
let streamGap = false;
const feedFollowing = { 'speech-list': true, timeline: true };

function escape(value) {
  return String(value ?? '未提供').replace(/[&<>"']/g, char => ({'&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'}[char]));
}
const label = value => escape(labels[value] ?? value);
const percent = value => value == null ? '未提供' : `${value}%`;
const number = value => value == null ? '未提供' : Number(value).toLocaleString('zh-CN', {maximumFractionDigits: 2});
const simTime = value => value == null ? '未提供' : `${number(value)} 模拟秒`;
const wallTime = value => value ? new Date(value).toLocaleString('zh-CN', {hour12: false}) : '未记录';
const usd = value => value == null ? '未提供' : `$${(value / 1e9).toFixed(6)}`;
const gigabytes = value => `${number(value / 1e9)} GB`;
const coords = point => point ? `${number(point.x)} / ${number(point.y)} / ${number(point.z)}` : '未提供';
const line = (key, html) => `<div class="fact"><dt>${key}</dt><dd>${html}</dd></div>`;
const optionalLine = (key, value, format = escape) => value == null ? '' : line(key, format(value));
const group = (title, html) => `<section class="group"><h3>${title}</h3>${html}</section>`;
const empty = text => `<p class="empty">${text}</p>`;
const actorLink = (id, name) => `<button class="link" data-actor="${id}">${escape(name)}</button>`;
const eventLink = (id, text) => `<button class="link" data-event="${id}">${text}</button>`;

function paint(id, html) {
  paintNode($(id), html);
}

function paintNode(node, html) {
  if (node.dataset.rendered === html) return;
  const open = [...node.querySelectorAll('details[data-evidence][open]')].map(block => block.dataset.evidence);
  node.innerHTML = html;
  node.dataset.rendered = html;
  for (const block of node.querySelectorAll('details[data-evidence]')) block.open = open.includes(block.dataset.evidence);
}

function worldTime(world) {
  if (world?.day == null || world.timeOfDay == null) return simTime(world?.time);
  const minutes = Math.floor(world.timeOfDay * 1440) % 1440;
  return `第 ${Math.floor(world.day) + 1} 日 ${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

function dispatchStatus(model) {
  if (!model) return '认知调度未接入';
  const state = model.status.state;
  if (state === 'quotaPaused') {
    const reason = model.status.quota.reason === 'budget' ? '累计模型额度不足' : '存储余量不足';
    return `${reason} · ${model.status.quota.saved ? '已保存并暂停' : '已暂停，等待保存'}`;
  }
  if (state !== 'running') return dispatchLabels[state] ?? state;
  if (model.rateLimitedUntil) return `限流等待至 ${wallTime(model.rateLimitedUntil)}`;
  if (model.preparingCheckpoint) return '等待检查点或恢复账本';
  return dispatchLabels.running;
}

function render() {
  const world = snapshot?.world;
  const actors = snapshot?.actors ?? [];
  const model = snapshot?.model;
  const dispatch = dispatchStatus(model);
  $('world-name').textContent = world?.name || '世界观察';
  $('run-state').textContent = world
    ? `${world.advancing ? '世界推进中' : '世界未推进'} · ${dispatch} · ${worldTime(world)}`
    : ({loading:'世界正在载入', noWorld:'请在游戏里进入世界', snapshotTooLarge:'快照超出展示上限',
      snapshotUnavailable:'世界快照暂不可用'}[snapshot?.state] ?? '正在读取当前世界');
  $('snapshot-time').textContent = world ? `快照 #${snapshot.version} · ${wallTime(snapshot.sampledAt)}` : '';
  $('stream-gap').hidden = !streamGap;
  $('stream-gap').textContent = streamGap ? '部分事件已离开内存窗口，页面从最新快照继续。' : '';
  const low = actors.filter(actor => actor.alive && actor.food != null && actor.food < 35).length;
  const active = actors.filter(actor => actor.alive && actor.action).length;
  paint('metrics', [['存活人物',world?.alive],['执行动作',world ? active : null],['低饱食',world ? low : null],
    ['模型在途',model?.status.networkInFlight]].map(([key, value]) => `<div><dt>${key}</dt><dd>${value ?? '—'}</dd></div>`).join(''));
  if (!actors.some(actor => actor.id === selectedId)) selectedId = actors[0]?.id ?? null;
  if (view === 'people') renderPeople(actors);
  if (view === 'dialogue') renderSpeech();
  if (view === 'events') renderEvents();
  if (view === 'world') renderWorld(world, model, dispatch);
  if (view === 'model') renderModel(model, dispatch);
  if (view === 'people' || $('detail-dialog').open) renderDetails();
}

function renderPeople(actors) {
  const search = $('search').value.trim().toLocaleLowerCase();
  const lowOnly = $('hungry').getAttribute('aria-pressed') === 'true';
  const matching = actors.filter(actor => `${actor.name} ${actor.id}`.toLocaleLowerCase().includes(search)
    && (!lowOnly || (actor.alive && actor.food != null && actor.food < 35)));
  matching.sort((a, b) => Number(b.alive) - Number(a.alive) || (a.food ?? Infinity) - (b.food ?? Infinity) || a.id - b.id);
  $('roster-count').textContent = snapshot?.world ? `${matching.length} / ${snapshot.world.actorCount} 人` : '';
  const actor = actors.find(item => item.id === selectedId);
  $('inspect-current').hidden = !actor;
  $('inspect-current').textContent = actor ? `查看 ${actor.name}` : '';
  paint('roster', matching.map(item => {
    const lastChange = snapshot.events.findLast(event => event.actorId === item.id && event.kind === 'body');
    const change = lastChange ? bodyChange(lastChange.data) : '';
    const first = item.inventory?.[0];
    const bag = first ? `${escape(first.name)} × ${first.count}` : item.inventory ? '空' : '';
    const food = item.food == null ? '' : `<span class="${item.food < 35 ? 'attention' : ''}">饱食 ${percent(item.food)}</span>`;
    return `<tr class="${item.id === selectedId ? 'selected' : ''}"><td>${actorLink(item.id,item.name)}<span class="sub">#${item.id}${item.alive ? '' : ' · 死亡'}</span></td>
      <td class="number">${number(item.position.x)} / ${number(item.position.z)}</td>
      <td>${food}<span class="sub">生命 ${percent(item.health)}</span>${change ? `<span class="sub">${change}</span>` : ''}</td>
      <td>${label(item.action?.name ?? item.task?.status ?? '暂无受控动作')}<span class="sub">${item.request ? label(item.request) : ''}</span></td><td class="inventory-col">${bag}</td></tr>`;
  }).join('') || '<tr><td colspan="5">暂无匹配人物</td></tr>');
}

function bodyChange(data) {
  const parts = [];
  if (data.beforeHealth !== data.health) parts.push(`生命 ${percent(data.beforeHealth)} → ${percent(data.health)}`);
  if (data.beforeFood !== data.food) parts.push(`饱食 ${percent(data.beforeFood)} → ${percent(data.food)}`);
  return parts.join(' · ');
}

function renderDetails() {
  const actor = snapshot?.actors.find(item => item.id === selectedId);
  const html = actor ? actorDetails(actor) : empty('选择一位人物查看状态');
  if (view === 'people') paint('inspector', html);
  if ($('detail-dialog').open) paint('dialog-content', html);
}

function actorDetails(actor) {
  const nav = `<nav class="tabs" aria-label="人物详情视图">${sections.map(([key,text]) => `<button data-detail="${key}" aria-pressed="${key === detail}">${text}</button>`).join('')}</nav>`;
  let body;
  if (detail === 'task') body = renderTask(actor);
  else if (detail === 'speech') body = renderSpeechRows(actor.id);
  else if (detail === 'memory') body = renderMemory(actor.cognition);
  else if (detail === 'history') body = renderEventRows(snapshot.events.filter(event => belongsTo(event, actor.id) && event.kind !== 'speech'));
  else body = renderOverview(actor);
  return `<h2>${escape(actor.name)} <span class="meta">#${actor.id}</span></h2><p class="meta">X / Y / Z · ${coords(actor.position)}${actor.alive ? '' : ' · 死亡'}</p>${nav}${body}`;
}

function renderOverview(actor) {
  const body = optionalLine('生命', actor.health, percent) + optionalLine('饱食度', actor.food, percent)
    + optionalLine('认知饥饿档', actor.hunger, hunger => `${label(hunger)} · 饥饿阈值 &lt;30%`)
    + optionalLine('体力', actor.stamina, stamina => percent(Math.round(stamina * 100)))
    + optionalLine('睡眠状态', actor.sleeping, sleeping => sleeping ? '睡眠中' : '清醒')
    + optionalLine('睡眠值', actor.sleep, number)
    + optionalLine('体温', actor.temperature, number)
    + optionalLine('湿润度', actor.wetness, number)
    + optionalLine('朝向向量', actor.facing, coords);
  const bag = actor.inventory == null ? '' : actor.inventory.length === 0 ? empty('背包为空')
    : `<dl>${actor.inventory.map(item => line(escape(item.name), `槽 ${item.slot} · ×${item.count}${item.selected ? ' · 选中' : ''}`)).join('')}</dl>`;
  const changes = snapshot.events.filter(event => event.actorId === actor.id && event.kind === 'body').slice(-3).reverse();
  const change = changes.map(event => `<p><time>${simTime(event.time)}</time> ${eventLink(event.sequence,bodyChange(event.data))}</p>`).join('');
  const goal = actor.cognition?.goal;
  const action = actor.action?.name;
  const activity = (action ? `<p>${label(action)}</p>` : '') + (actor.request ? `<p class="sub">模型 · ${label(actor.request)}</p>` : '');
  return (goal ? group('当前目标', `<p>${escape(goal)}</p>`) : '') + group('身体与位置', `<dl>${body}</dl>${change}`)
    + (activity ? group('当前行动', activity) : '')
    + (bag ? group('背包', bag + (actor.inventoryCount > actor.inventory.length ? empty(`显示前 ${actor.inventory.length} 个已占用栏位`) : '')) : '');
}

function renderTask(actor) {
  const task = actor.task;
  const current = task && task.status !== 'idle' ? `<dl>${line('状态', label(task.status))}${line('进度', `${task.completedSteps} / ${task.totalSteps}`)}
    ${optionalLine('最近进展',task.lastProgressTime,simTime)}${task.failure && task.failure !== 'none' ? line('任务失败',label(task.failure)) : ''}
    ${actor.action?.target ? line('动作目标',coords(actor.action.target)) : ''}</dl>
    <ol>${task.steps.map(step => `<li>${label(step.name)} · ${label(step.status)}<span class="sub">尝试 ${step.attempts} 次 · 余 ${step.retriesRemaining} 次 · 影响数量 ${step.affectedCount}${step.atomicFailure && step.atomicFailure !== 'none' ? ` · ${label(step.atomicFailure)}` : ''}</span></li>`).join('')}</ol>`
    : '';
  const model = optionalLine('请求',actor.request,label) + optionalLine('下次定时复查',actor.cognition?.reviewAt,simTime);
  let accepted = actor.cognition?.decisionTruncated ? empty('最近一次决定超出单条展示上限') : '';
  if (actor.cognition?.decision) {
    const saved = JSON.parse(actor.cognition.decision);
    const input = saved.input;
    const evidence = [['局部观察',input.observation],['实际提供的记忆',input.memories],['实际提供的消息',input.messages],['原计划',saved.decision.steps]];
    const expandable = evidence.filter(([, value]) => value != null && (!Array.isArray(value) || value.length > 0)).map(([key,value]) =>
      `<details data-evidence="${actor.id}-${key}"><summary>${key}</summary><pre>${escape(JSON.stringify(value,null,2))}</pre></details>`).join('');
    const outcomes = saved.taskId && snapshot.events.some(event => event.kind === 'action' && event.data.taskId === saved.taskId && event.actorId === actor.id);
    accepted = `<dl>${optionalLine('观察时点',input.gameTime,simTime)}${optionalLine('触发条件',input.reasons?.join(' · ') || null)}
      ${line('决定',escape(saved.decision.task))}${line('模型给出的理由',escape(saved.decision.reason))}</dl>${expandable}
      ${outcomes ? `<button class="link" data-task-events="${escape(saved.taskId)}" data-task-actor="${actor.id}">查看关联动作结果</button>` : ''}`;
  }
  return (current ? group('任务进展', current) : '') + (model ? group('模型与复查', `<dl>${model}</dl>`) : '')
    + (accepted ? group('最近已采纳决定', accepted) : '') || empty('暂无可查看的任务或决定');
}

function renderMemory(cognition) {
  const notes = list => list?.length ? list.map(note => `<p>${escape(note.text)}<span class="sub">${simTime(note.at)} · ${escape(note.kind)} · ${escape(note.source)}</span></p>`).join('')
    : empty(cognition ? '暂无记录' : '人物没有可读取的认知状态');
  return group('个人记述', notes(cognition?.memories)) + group('未完承诺', notes(cognition?.commitments));
}

function belongsTo(event, id) {
  return event.actorId === id || event.data.recipient?.id === id || event.data.listeners?.some(listener => listener.id === id);
}

function collectSpeeches(actorId = null) {
  const speeches = new Map();
  for (const event of snapshot?.speech ?? snapshot?.events ?? []) {
    if (event.kind !== 'speech' || (actorId != null && !belongsTo(event, actorId))) continue;
    const key = event.data.speechId;
    if (!speeches.has(key)) speeches.set(key, []);
    speeches.get(key).push(event);
  }
  return [...speeches.values()];
}

function renderSpeechRows(actorId) {
  return collectSpeeches(actorId).reverse().map(parts => {
      const first = parts[0], last = parts.at(-1);
      const lines = parts.filter(part => part.data.text).map(part => `<p>${escape(part.data.text)}<span class="sub">实际听众 · ${part.data.listeners.map(listener => actorLink(listener.id, listener.name)).join('、') || '无人听见'}</span></p>`).join('');
      return `<article class="entry"><time>${simTime(first.time)}</time><div>${actorLink(first.actorId,first.actorName)} <span class="meta">${label(first.data.mode)} · ${label(last.data.status)}</span>${first.data.chunk > 0 ? '<p class="sub">窗口从中间片段开始</p>' : ''}${lines}</div></article>`;
    }).join('') || empty('当前窗口还没有对话');
}

function updateFeedState(id) {
  const following = feedFollowing[id];
  const state = $(id + '-state');
  state.dataset.following = following;
  state.textContent = following ? '跟随最新' : '浏览历史';
  $(id + '-latest').hidden = following;
}

function renderFeed(id, rows, message) {
  const feed = $(id);
  if (!rows.length) {
    if (feed.firstElementChild?.textContent !== message || feed.firstElementChild?.className !== 'empty') {
      feed.innerHTML = empty(message);
    }
    updateFeedState(id);
    return;
  }
  const previous = new Map([...feed.children].map(node => [node.dataset.key, node]));
  const keys = new Set(rows.map(row => row.key));
  for (const [key, node] of previous) if (!keys.has(key)) node.remove();
  let cursor = feed.firstElementChild;
  for (const row of rows) {
    let node = previous.get(row.key);
    if (!node) {
      node = document.createElement('article');
      node.className = 'feed-row';
      node.dataset.key = row.key;
      node.tabIndex = -1;
      if (feedFollowing[id] && previous.size && !previous.has(undefined)) node.dataset.new = 'true';
    }
    const pill = `<button class="link actor-pill" data-actor="${row.actorId}"${row.detail ? ` data-detail-target="${row.detail}"` : ''}>${escape(row.actorName)} <span class="meta">#${row.actorId}</span></button>`;
    paintNode(node, `${pill}<div class="feed-content">${row.content}</div>`);
    if (row.highlight) node.dataset.highlight = 'true';
    else delete node.dataset.highlight;
    if (node !== cursor) feed.insertBefore(node, cursor);
    cursor = node.nextElementSibling;
  }
  if (feedFollowing[id]) feed.scrollTop = feed.scrollHeight;
  updateFeedState(id);
}

function renderSpeech() {
  if (!feedFollowing['speech-list']) return;
  const search = $('speech-search').value.trim().toLocaleLowerCase();
  const rows = collectSpeeches().map(parts => {
    const first = parts[0], last = parts.at(-1);
    const text = parts.map((part, index) => part.data.text
      ? (index && part.data.chunk > parts[index - 1].data.chunk + 1 ? '…' : '') + part.data.text : '').join('');
    const listeners = parts.flatMap(part => part.data.listeners);
    if (search && ![first.actorName, first.actorId, text, ...listeners.map(listener => listener.name)].join(' ').toLocaleLowerCase().includes(search)) return null;
    const speakerDetails = parts.filter(part => part.data.text).map(part => `<p>${escape(part.data.text)}<span class="sub">实际听众 · ${part.data.listeners.map(listener => actorLink(listener.id, listener.name)).join('、') || '无人听见'}</span></p>`).join('');
    const count = new Set(listeners.map(listener => listener.id)).size;
    return { key: first.data.speechId, actorId: first.actorId, actorName: first.actorName, content:
      `<p>${escape(text || '发言中断')}</p><div class="feed-meta"><time>${simTime(first.time)}</time><span class="meta">${label(first.data.mode)} · ${label(last.data.status)}${first.data.chunk > 0 ? ' · 开头已离开窗口' : ''}</span></div>
      ${speakerDetails ? `<details data-evidence="${escape(first.data.speechId)}"><summary class="meta">${count ? `${count} 位听众` : '无人听见'}</summary>${speakerDetails}</details>` : ''}` };
  }).filter(Boolean);
  renderFeed('speech-list', rows, search ? '没有匹配的对话' : '当前窗口还没有对话');
}

function eventContent(event) {
  const data = event.data;
  if (event.kind === 'body') return `${bodyChange(data)}<span class="sub">来自相邻快照 · 原因未记录</span>`;
  if (event.kind === 'transfer') return `${label(data.status)} · ${escape(data.item)} ×${data.count} · 接收者 ${actorLink(data.recipient.id,data.recipient.name)}<span class="sub">提议编号 ${escape(data.offerId)}</span>`;
  return `${label(data.action.name)}${data.action.item ? ` ${escape(data.action.item)}` : ''} · ${label(data.status)}${data.failure !== 'none' ? ` · ${label(data.failure)}` : ''}
      ${data.affectedCount > 0 ? `<span class="sub">影响数量 ${data.affectedCount}</span>` : ''}${data.items?.map(item => `<span class="sub">获得 ${escape(item.name)} ×${item.count}</span>`).join('') ?? ''}`;
}

function renderEventRows(events) {
  return [...events].reverse().map(event =>
    `<article class="entry"><time>${simTime(event.time)}</time><div>${actorLink(event.actorId,event.actorName)}<p>${eventContent(event)}</p>${eventLink(event.sequence, '查看全局事件')}</div></article>`
  ).join('') || empty('内存窗口内暂无匹配事件');
}

function renderEvents(force = false) {
  if (!force && !feedFollowing.timeline) return;
  const kind = $('event-filter').value;
  const events = (snapshot?.events ?? []).filter(event => event.kind !== 'speech' && (kind === 'all' || event.kind === kind)
    && (!eventTask || (event.actorId === eventTask.actor && event.data.taskId === eventTask.id)));
  $('clear-task').hidden = !eventTask;
  const rows = events.map(event => ({ key: String(event.sequence), actorId: event.actorId, actorName: event.actorName,
    detail: 'history', highlight: eventFocus === event.sequence, content:
      `<div>${eventContent(event)}</div><div class="feed-meta"><time>${simTime(event.time)}</time></div>` }));
  renderFeed('timeline', rows, '内存窗口内暂无匹配事件');
}

function renderWorld(world, model, dispatch) {
  if (!world) return paint('world', empty('暂无世界快照'));
  paint('world', group('环境与时钟', `<dl>${line('世界名称',escape(world.name))}${line('游戏模式',escape(world.gameMode))}${line('世界日历',worldTime(world))}
    ${line('本次模拟时钟',simTime(world.time))}${line('世界累计时长',simTime(world.elapsed))}${line('时间倍率',number(world.timeFactor))}
    ${optionalLine('降水强度',world.precipitation,intensity => percent(Math.round(intensity * 100)))}</dl>`)
    + group('当前已加载范围', `<dl>${optionalLine('已分配地形区块',world.chunks,number)}${line('存活人物',number(world.alive))}${line('其他活体生物',number(world.creatures))}
    ${line('地面掉落堆数',number(world.dropStacks))}${line('地面食物数量',number(world.droppedFood))}${line('人物背包内食物',number(world.foodInBags))}</dl>`)
    + group('存档与运行', `<dl>${line('世界推进',world.advancing ? '推进中' : '未推进')}${line('模型派发',escape(dispatch))}${optionalLine('最近原生世界存档',world.savedAt,wallTime)}
    ${optionalLine('最近认知检查点',model?.status.checkpointTime,simTime)}${line('快照序号',`#${snapshot.version}`)}${line('事件记录',`${snapshot.events.length} 条 · 内存窗口`)}</dl>`));
}

function renderModel(model, dispatch) {
  if (!model) return paint('model',empty('当前世界未绑定全局认知调度，暂无请求和费用账本'));
  const status = model.status;
  const quota = status.quota;
  paint('model', group('调度',`<dl>${line('派发状态',escape(dispatch))}${line('网络请求 / 并发上限',`${status.networkInFlight} / ${model.parallelism}`)}
    ${line('占用的调度槽位',status.inFlight)}${line('本次进程已发送',status.requests)}${line('已采纳决定',status.applied)}${line('账本写入中',status.ledgerPending ? '是' : '否')}${status.fault ? line('故障',escape(status.fault)) : ''}</dl>`)
    + group('费用 · 美元估算',`<dl>${line(quota ? '共享费用上限' : '预算',usd(status.budget))}${line('本实验已知费用',usd(status.spent))}${line('本实验在途预留',usd(status.reserved))}${line('本实验未知用量预留',usd(status.unknown))}${optionalLine('其他实验已占额度',quota?.otherFees,usd)}${optionalLine('共享剩余额度',quota?.availableFees,usd)}${line('价格版本',escape(model.priceVersion))}</dl>`)
    + (quota ? group('共享存储',`<dl>${line('已占用',gigabytes(quota.storageUsed))}${line('容量上限',gigabytes(quota.storageLimit))}
      ${line('保存与在途写入预留',gigabytes(quota.storageReserve))}${line('磁盘可用空间',gigabytes(quota.diskFree))}</dl>`) : '')
    + group('最近 32 次已入账尝试',model.recentAttempts.length ? [...model.recentAttempts].reverse().map(attempt => {
      const name = snapshot.actors.find(actor => actor.id === attempt.actorId)?.name ?? `#${attempt.actorId}`;
      const details = [attempt.model && escape(attempt.model), attempt.inputTokens == null ? null : `输入 ${number(attempt.inputTokens)} token`,
        attempt.outputTokens == null ? null : `输出 ${number(attempt.outputTokens)} token`].filter(Boolean).join(' · ');
      return `<article class="entry"><time>${attempt.sentAt ? wallTime(attempt.sentAt) : ''}</time><div>${actorLink(attempt.actorId,name)} · ${label(attempt.state)}${details ? `<span class="sub">${details}</span>` : ''}${attempt.state === 'completed' && attempt.charge != null ? `<span class="sub">${usd(attempt.charge)}</span>` : ''}</div></article>`;
    }).join('') : empty('暂无已入账尝试')));
}

function show(nextView) {
  view = nextView;
  for (const button of document.querySelectorAll('[data-view]')) {
    const enabled = button.dataset.view === view;
    button.setAttribute('aria-pressed', enabled);
    $(button.dataset.view).hidden = !enabled;
  }
  render();
}

function followLatest(id) {
  feedFollowing[id] = true;
  if (id === 'speech-list') renderSpeech();
  else {
    eventFocus = null;
    renderEvents();
  }
}

function inspect(id, section = 'overview') {
  if (!snapshot?.actors.some(actor => actor.id === id)) return;
  selectedId = id;
  detail = section;
  if (compact.matches) {
    if (!$('detail-dialog').open) $('detail-dialog').showModal();
    renderDetails();
  } else show('people');
}

document.addEventListener('click', event => {
  const button = event.target.closest('button');
  if (!button) return;
  const data = button.dataset;
  if (data.view) show(data.view);
  if (data.latest) {
    followLatest(data.latest);
    $(data.latest).focus({preventScroll: true});
  }
  if (data.actor) inspect(Number(data.actor), data.detailTarget);
  if (data.detail) { detail = data.detail; renderDetails(); }
  if (data.event || data.taskEvents) {
    eventFocus = data.event ? Number(data.event) : null;
    eventTask = data.taskEvents ? {id: data.taskEvents, actor: Number(data.taskActor)} : null;
    feedFollowing.timeline = eventFocus == null;
    $('event-filter').value = 'all';
    if ($('detail-dialog').open) $('detail-dialog').close();
    show('events');
    if (eventFocus != null) renderEvents(true);
    const highlighted = $('timeline').querySelector('[data-highlight=true]');
    highlighted?.focus({preventScroll: true});
    highlighted?.scrollIntoView({block: 'nearest'});
  }
  if (data.clearTask !== undefined) { eventTask = null; feedFollowing.timeline = true; renderEvents(); }
});
$('inspect-current').addEventListener('click', () => inspect(selectedId,detail));
$('hungry').addEventListener('click', () => {
  $('hungry').setAttribute('aria-pressed', $('hungry').getAttribute('aria-pressed') !== 'true');
  renderPeople(snapshot?.actors ?? []);
});
$('search').addEventListener('input', () => renderPeople(snapshot?.actors ?? []));
$('speech-search').addEventListener('input', () => { feedFollowing['speech-list'] = true; renderSpeech(); });
$('event-filter').addEventListener('change', () => { eventFocus = null; feedFollowing.timeline = true; renderEvents(); });
for (const id of Object.keys(feedFollowing)) {
  const feed = $(id);
  feed.addEventListener('scroll', () => {
    if (!feed.clientHeight) return;
    const following = feed.scrollHeight - feed.scrollTop - feed.clientHeight <= 8;
    if (following === feedFollowing[id]) return;
    if (following) followLatest(id);
    else {
      feedFollowing[id] = false;
      updateFeedState(id);
    }
  }, {passive: true});
}

async function poll() {
  try {
    const response = await fetch('/api/snapshot', {cache: 'no-store', signal: AbortSignal.timeout(3000)});
    if (!response.ok) throw new Error('snapshot');
    const next = await response.json();
    if (next.session !== snapshot?.session) {
      selectedId = eventFocus = eventTask = null;
      detail = 'overview';
      streamGap = false;
      for (const id of Object.keys(feedFollowing)) {
        feedFollowing[id] = true;
        $(id).replaceChildren();
      }
      if ($('detail-dialog').open) $('detail-dialog').close();
    } else if (next.sequence > snapshot?.sequence) {
      const isNew = event => event.sequence > snapshot.sequence;
      const received = next.events.filter(isNew).length + (next.speech?.filter(isNew).length ?? 0);
      if (received < next.sequence - snapshot.sequence) streamGap = true;
    }
    snapshot = next;
    const stale = next.sampledAt && Date.now() - Date.parse(next.sampledAt) > 3000;
    $('connection').textContent = stale ? '快照未更新' : '本机已连接';
    render();
  } catch {
    $('connection').textContent = snapshot?.world ? '连接中断 · 显示最后快照' : '等待游戏连接';
  } finally {
    setTimeout(poll, 1000);
  }
}
poll();
