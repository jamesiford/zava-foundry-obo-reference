(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const PENDING_KEY = 'compass.pendingQuestion';

  const branchLabels = {
    sharePointRetrieval: 'Searching SharePoint documents',
    sharePointKnowledge: 'Searching the knowledge base',
    zavaApi: 'Reading your accounts',
  };
  const outcomeMarks = { succeeded: '\u2713', failed: '\u2717', withheld: '\u25CB' };
  const outcomeText = {
    succeeded: 'done',
    failed: 'could not complete',
    withheld: 'not available to you',
  };
  const errorText = {
    sign_in_required: 'Your sign-in is no longer valid. Please sign in again.',
    claims_challenge: 'Extra verification is required. Please try again.',
    member_not_linked: 'Your sign-in is not linked to a member record.',
    service_unavailable: 'A back-end service is unavailable. Please try again shortly.',
    timeout: 'The briefing took too long. Please try again.',
    invalid_request: 'That question could not be sent. Check it and try again.',
    internal_error: 'Something went wrong. Please try again.',
  };

  let client;
  let config;
  let account;

  const show = (id, visible) => { $(id).hidden = !visible; };
  const setStatus = (text) => { $('status').textContent = text; };

  async function start() {
    let response;
    try {
      response = await fetch('/api/config', { cache: 'no-store' });
    } catch {
      return fatal('The service could not be reached.');
    }
    if (!response.ok) {
      return fatal('Sign-in is not configured for this deployment yet.');
    }
    config = await response.json();

    client = new msal.PublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: window.location.origin + '/',
        navigateToLoginRequestUrl: false,
      },
      cache: { cacheLocation: 'sessionStorage' },
    });
    await client.initialize();

    try {
      const redirected = await client.handleRedirectPromise();
      if (redirected?.account) {
        client.setActiveAccount(redirected.account);
      }
    } catch {
      return fatal('Sign-in did not complete. Please try again.');
    }

    account = client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null;
    if (account) {
      client.setActiveAccount(account);
    }
    render();
  }

  function fatal(message) {
    show('signedOut', false);
    show('signedIn', false);
    show('fatal', true);
    $('fatal').textContent = message;
  }

  function render() {
    show('signedOut', !account);
    show('signedIn', !!account);
    show('account', !!account);
    if (account) {
      $('who').textContent = account.name || 'Signed in';
      const pending = sessionStorage.getItem(PENDING_KEY);
      if (pending) {
        sessionStorage.removeItem(PENDING_KEY);
        $('question').value = pending;
      }
    }
  }

  async function getToken(claims) {
    const request = { scopes: [config.scope], account };
    if (claims) {
      request.claims = claims;
    }
    try {
      return (await client.acquireTokenSilent(request)).accessToken;
    } catch (error) {
      if (error instanceof msal.InteractionRequiredAuthError || claims) {
        sessionStorage.setItem(PENDING_KEY, $('question').value);
        await client.acquireTokenRedirect(request);
        return null;
      }
      throw error;
    }
  }

  function decodeClaims(encoded) {
    try {
      return atob(encoded.replace(/-/g, '+').replace(/_/g, '/'));
    } catch {
      return null;
    }
  }

  function claimsFrom(response) {
    const header = response.headers.get('WWW-Authenticate') || '';
    const match = /claims="([A-Za-z0-9+/_=-]+)"/.exec(header);
    return match ? decodeClaims(match[1]) : null;
  }

  async function post(question, token) {
    return fetch('/api/briefing', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer ' + token,
      },
      body: JSON.stringify({ question }),
    });
  }

  // A step-up demand arrives two ways: a 401 with WWW-Authenticate before the stream starts, or an `error` event with
  // claims once a branch has raised it. Either way the token is re-acquired with those claims and the call is retried
  // once; a second demand is shown as an error.
  async function ask(question) {
    resetResult();
    setBusy(true);
    try {
      let claims = null;
      for (let attempt = 0; attempt < 2; attempt++) {
        const token = await getToken(claims);
        if (!token) {
          return;
        }
        const response = await post(question, token);

        if (response.status === 401 && attempt === 0) {
          claims = claimsFrom(response);
          if (claims) {
            continue;
          }
        }
        if (!response.ok) {
          const body = await response.json().catch(() => ({}));
          return showError(body.error, response.status);
        }

        claims = await readEvents(response, attempt === 0);
        if (!claims) {
          return;
        }
        resetResult();
      }
    } catch {
      showError('internal_error');
    } finally {
      setBusy(false);
    }
  }

  // Returns the decoded claims when the stream ended with a step-up demand that may be retried, otherwise null.
  async function readEvents(response, canRetry) {
    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        return null;
      }
      buffer += value;
      let split;
      while ((split = buffer.indexOf('\n\n')) >= 0) {
        const claims = handleBlock(buffer.slice(0, split), canRetry);
        buffer = buffer.slice(split + 2);
        if (claims) {
          return claims;
        }
      }
    }
  }

  function handleBlock(block, canRetry) {
    let name = 'message';
    let data = '';
    for (const line of block.split('\n')) {
      if (line.startsWith('event: ')) {
        name = line.slice(7);
      } else if (line.startsWith('data: ')) {
        data += line.slice(6);
      }
    }
    let payload;
    try {
      payload = JSON.parse(data);
    } catch {
      return null;
    }
    if (name === 'progress') {
      onProgress(payload);
    } else if (name === 'answer') {
      onAnswer(payload);
    } else if (name === 'error') {
      const claims = canRetry && payload.claims ? decodeClaims(payload.claims) : null;
      if (claims) {
        return claims;
      }
      showError(payload.code);
    }
    return null;
  }

  const rows = new Map();

  function rowFor(key, label) {
    let row = rows.get(key);
    if (!row) {
      row = document.createElement('li');
      const mark = document.createElement('span');
      mark.className = 'mark';
      mark.textContent = '\u2026';
      const text = document.createElement('span');
      text.textContent = label;
      row.append(mark, text);
      $('progress').append(row);
      rows.set(key, row);
    }
    return row;
  }

  function onProgress(event) {
    show('progressPanel', true);
    const detail = document.createElement('li');
    detail.textContent = [event.stage, event.branch, event.outcome, event.elapsedMilliseconds + ' ms', event.route]
      .filter(Boolean)
      .join(' \u00B7 ');
    $('details').append(detail);

    if (event.branch && branchLabels[event.branch]) {
      const row = rowFor(event.branch, branchLabels[event.branch]);
      if (event.outcome && outcomeMarks[event.outcome]) {
        row.firstChild.textContent = outcomeMarks[event.outcome];
        row.lastChild.textContent = branchLabels[event.branch] + ' \u2014 ' + outcomeText[event.outcome];
      }
    } else if (event.stage === 'composing') {
      rowFor('composing', 'Writing your briefing');
    } else if (event.stage === 'completed') {
      const composing = rows.get('composing');
      if (composing) {
        composing.firstChild.textContent = outcomeMarks.succeeded;
      }
    }
  }

  // The answer is markdown-ish text from the briefing: bullets, headings and **bold**. It is built from DOM nodes and text
  // nodes only, so nothing in it can be parsed as markup or run as script.
  function appendInline(parent, text) {
    text.split(/\*\*(.+?)\*\*/).forEach((part, i) => {
      if (!part) {
        return;
      }
      if (i % 2) {
        const strong = document.createElement('strong');
        strong.textContent = part;
        parent.append(strong);
      } else {
        parent.append(document.createTextNode(part));
      }
    });
  }

  function renderAnswer(container, text) {
    container.replaceChildren();
    let list = null;
    for (const raw of String(text).split('\n')) {
      const line = raw.trimEnd();
      const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
      if (bullet) {
        if (!list) {
          list = document.createElement('ul');
          container.append(list);
        }
        const item = document.createElement('li');
        appendInline(item, bullet[1]);
        list.append(item);
        continue;
      }
      list = null;
      if (!line.trim()) {
        continue;
      }
      const heading = /^\s{0,3}#{1,6}\s+(.*)$/.exec(line);
      const block = document.createElement(heading ? 'h3' : 'p');
      appendInline(block, heading ? heading[1] : line);
      container.append(block);
    }
  }

  function onAnswer(answer) {
    renderAnswer($('answer'), answer.answer);
    show('answerPanel', true);
    const list = $('citations');
    list.replaceChildren();
    for (const citation of answer.citations || []) {
      if (!/^https?:\/\//i.test(citation.url || '')) {
        continue;
      }
      const item = document.createElement('li');
      const link = document.createElement('a');
      link.href = citation.url;
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      link.textContent = citation.title || citation.url;
      item.append(link);
      list.append(item);
    }
    show('sourcesPanel', list.children.length > 0);
  }

  function showError(code, status) {
    const text = errorText[code] || (status === 401 ? errorText.sign_in_required : errorText.internal_error);
    $('error').textContent = text;
    show('error', true);
  }

  function resetResult() {
    rows.clear();
    $('progress').replaceChildren();
    $('details').replaceChildren();
    $('answer').textContent = '';
    $('citations').replaceChildren();
    show('progressPanel', false);
    show('answerPanel', false);
    show('sourcesPanel', false);
    show('error', false);
  }

  function setBusy(busy) {
    $('ask').disabled = busy;
    $('question').disabled = busy;
    setStatus(busy ? 'Working on your briefing\u2026' : '');
  }

  $('signIn').addEventListener('click', () => client.loginRedirect({ scopes: [config.scope] }));
  $('signOut').addEventListener('click', () => client.logoutRedirect({ account, postLogoutRedirectUri: window.location.origin + '/' }));
  $('form').addEventListener('submit', (event) => {
    event.preventDefault();
    const question = $('question').value.trim();
    if (question) {
      ask(question);
    }
  });
  for (const chip of document.querySelectorAll('[data-question]')) {
    chip.addEventListener('click', () => {
      $('question').value = chip.dataset.question;
      $('question').focus();
    });
  }

  start();
})();
