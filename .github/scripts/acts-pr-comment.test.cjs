const assert = require('node:assert/strict');
const { mkdtempSync, writeFileSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join } = require('node:path');
const { test } = require('node:test');
const { buildComment, publishComment } = require('./acts-pr-comment.cjs');

const run = {
  id: 123,
  run_attempt: 1,
  name: 'ACTS',
  path: '.github/workflows/acts.yaml',
  event: 'pull_request',
  status: 'completed',
  conclusion: 'success',
  head_sha: 'a'.repeat(40),
  head_branch: 'feature',
  head_repository: { full_name: 'contributor/a2a-dotnet', owner: { login: 'contributor' } },
  repository: { full_name: 'a2aproject/a2a-dotnet' },
  pull_requests: [],
};

function report(transport, tests = [{ id: 'BASE', level: 'must', result: 'pass' }]) {
  const byLevel = {};
  for (const level of ['must', 'should', 'may']) {
    const selected = tests.filter(test => test.level === level);
    byLevel[level] = {
      total: selected.length,
      passed: selected.filter(test => test.result === 'pass').length,
      failed: selected.filter(test => test.result === 'fail').length,
      errors: selected.filter(test => test.result === 'error').length,
      skipped: selected.filter(test => test.result === 'skip').length,
    };
  }
  return {
    sdk: { name: 'a2a-dotnet' },
    transport,
    summary: { total: tests.length, by_level: byLevel },
    suites: [{ id: 'example', tests }],
  };
}

function reports() {
  return [report('jsonrpc'), report('rest'), report('grpc')];
}

test('renders SHOULD failures and MAY errors, without listing MUST failures or skips', () => {
  const input = reports();
  input[1] = report('rest', [
    { id: 'REST-SUB-GET-001', level: 'should', result: 'fail', failure: { message: 'Expected SSE', step_id: 'subscribe' } },
    { id: 'PUSH-CFG-002', level: 'may', result: 'error', failure: { message: 'Config missing' } },
    { id: 'MUST-FAIL', level: 'must', result: 'fail' },
    { id: 'SHOULD-SKIP', level: 'should', result: 'skip' },
  ]);
  const result = buildComment(input, run);
  assert.equal(result.failureCount, 2);
  assert.match(result.body, /\| SHOULD \| rest \| REST-SUB-GET-001 \| fail \| Expected SSE/);
  assert.match(result.body, /\| MAY \| rest \| PUSH-CFG-002 \| error \| Config missing/);
  assert.doesNotMatch(result.body, /MUST-FAIL|SHOULD-SKIP/);
  assert.match(result.body, /actions\/runs\/123/);
  assert.match(result.body, /MUST.*unchanged/);
});

test('escapes untrusted report text so it cannot add rows, mentions, or HTML', () => {
  const input = reports();
  input[0] = report('jsonrpc', [{
    id: 'TEST|<script>',
    level: 'should',
    result: 'fail',
    failure: { message: '@maintainer\n[click](https://example.com) <!-- injected --> `code`' },
  }]);
  const { body } = buildComment(input, run);
  assert.doesNotMatch(body, /<script>|@maintainer|\[click\]|<!-- injected -->|`code`/);
  assert.match(body, /TEST&#124;&#60;script&#62;/);
});

test('uses the validated report transport rather than a test-supplied transport', () => {
  const input = reports();
  input[0] = report('jsonrpc', [{
    id: 'OPTIONAL', level: 'may', result: 'fail', transport: '| @maintainer',
  }]);
  const { body } = buildComment(input, run);
  assert.match(body, /\| MAY \| jsonrpc \| OPTIONAL \|/);
  assert.doesNotMatch(body, /@maintainer/);
});

test('bounds large failure lists and links to the full run', () => {
  const input = reports();
  input[0] = report('jsonrpc', Array.from({ length: 500 }, (_, index) => ({
    id: `FAIL-${index}`,
    level: 'may',
    result: 'fail',
    failure: { message: '<'.repeat(2000) },
  })));
  const result = buildComment(input, run);
  assert.equal(result.failureCount, 500);
  assert.ok(result.body.length < 60000);
  assert.match(result.body, /omitted.*full reports/);
});

test('does not claim all tests pass when only advisory failures are absent', () => {
  const input = reports();
  input[0] = report('jsonrpc', [{ id: 'REQUIRED', level: 'must', result: 'fail' }]);
  const result = buildComment(input, run);
  assert.equal(result.failureCount, 0);
  assert.match(result.body, /No SHOULD or MAY failures/);
  assert.doesNotMatch(result.body, /all tests pass/i);
});

test('rejects incomplete, duplicate, inconsistent, and malformed reports rather than clearing warnings', () => {
  assert.throws(() => buildComment(reports().slice(1), run), /transport/i);
  assert.throws(() => buildComment([report('rest'), report('rest'), report('grpc')], run), /transport/i);
  const inconsistent = reports();
  inconsistent[0].summary.by_level.should.failed = 1;
  assert.throws(() => buildComment(inconsistent, run), /summary/i);
  const malformed = reports();
  malformed[0].suites[0].tests[0].result = 'unknown';
  assert.throws(() => buildComment(malformed, run), /test/i);
});

const pull = {
  number: 569,
  state: 'open',
  head: { sha: run.head_sha, repo: { full_name: run.head_repository.full_name } },
  base: { repo: { full_name: run.repository.full_name } },
};

async function withPublisher(options, action) {
  const directory = mkdtempSync(join(tmpdir(), 'acts-pr-comment-'));
  const input = options.reports ?? reports();
  for (const item of input) {
    writeFileSync(join(directory, `acts-report-${item.transport}.json`), JSON.stringify(item));
  }
  const state = { comments: options.comments ?? [], writes: [], logs: [] };
  const api = {
    actions: {
      listWorkflowRuns: async params => {
        assert.equal(params.workflow_id, 'acts.yaml');
        assert.equal(params.event, 'pull_request');
        assert.equal(params.head_sha, run.head_sha);
        assert.equal(params.branch, run.head_branch);
        return options.workflowRuns ?? [options.run ?? run];
      },
    },
    pulls: {
      list: async params => {
        assert.equal(params.head, 'contributor:feature');
        return options.pulls ?? [pull];
      },
      get: async () => ({ data: options.currentPull ?? pull }),
    },
    issues: {
      listComments: async params => {
        assert.equal(params.issue_number, 569);
        return state.comments;
      },
      createComment: async params => state.writes.push({ operation: 'create', ...params }),
      updateComment: async params => state.writes.push({ operation: 'update', ...params }),
    },
  };
  const github = { rest: api, paginate: async (method, params) => method(params) };
  const context = {
    repo: { owner: 'a2aproject', repo: 'a2a-dotnet' },
    payload: { workflow_run: options.run ?? run },
  };
  try {
    await publishComment({
      github, context,
      core: { info: message => state.logs.push(message) },
      reportDirectory: directory,
    });
    await action(state);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

function failingReports() {
  const input = reports();
  input[1] = report('rest', [{
    id: 'REST-SUB-GET-001', level: 'should', result: 'fail',
    failure: { message: 'Expected SSE' },
  }]);
  return input;
}

function botComment(id = 7, metadata = 'run:122 attempt:1') {
  return {
    id,
    user: { login: 'github-actions[bot]', type: 'Bot' },
    body: `<!-- a2a-acts-advisories -->\n<!-- ${metadata} -->\nOld result`,
  };
}

test('finds fork PRs without workflow_run.pull_requests and creates an advisory comment', async () => {
  await withPublisher({ reports: failingReports() }, state => {
    assert.equal(state.writes.length, 1);
    assert.equal(state.writes[0].operation, 'create');
    assert.equal(state.writes[0].issue_number, 569);
    assert.match(state.writes[0].body, /REST-SUB-GET-001/);
  });
});

test('selects the associated PR when two open PRs share a source branch and head SHA', async () => {
  await withPublisher({
    reports: failingReports(),
    pulls: [{ ...pull, number: 570 }, pull],
    run: { ...run, pull_requests: [{ number: 569 }] },
  }, state => {
    assert.equal(state.writes.length, 1);
    assert.equal(state.writes[0].issue_number, 569);
  });
});

test('skips ambiguous PR matches when run associations are unavailable', async () => {
  await withPublisher({
    reports: failingReports(),
    pulls: [pull, { ...pull, number: 570 }],
  }, state => {
    assert.deepEqual(state.writes, []);
    assert.ok(state.logs.length > 0);
  });
});

test('does not fall back to an unrelated PR when the associated PR no longer matches', async () => {
  await withPublisher({
    reports: failingReports(),
    run: { ...run, pull_requests: [{ number: 570 }] },
  }, state => assert.deepEqual(state.writes, []));
});

test('updates the bot comment and marks resolved advisory failures as clear', async () => {
  await withPublisher({ comments: [botComment()] }, state => {
    assert.equal(state.writes.length, 1);
    assert.equal(state.writes[0].operation, 'update');
    assert.equal(state.writes[0].comment_id, 7);
    assert.match(state.writes[0].body, /No SHOULD or MAY failures/);
  });
});

test('avoids a new comment when there are no advisory failures', async () => {
  await withPublisher({}, state => assert.deepEqual(state.writes, []));
});

test('does not overwrite user-authored marker comments', async () => {
  const userComment = botComment();
  userComment.user = { login: 'contributor', type: 'User' };
  await withPublisher({ reports: failingReports(), comments: [userComment] }, state => {
    assert.equal(state.writes[0].operation, 'create');
  });
});

test('skips runs for old commits, closed PRs, or different forks', async () => {
  for (const currentPull of [
    { state: 'open', head: { sha: 'b'.repeat(40), repo: { full_name: run.head_repository.full_name } } },
    { state: 'closed', head: { sha: run.head_sha, repo: { full_name: run.head_repository.full_name } } },
    { state: 'open', head: { sha: run.head_sha, repo: { full_name: 'other/a2a-dotnet' } } },
  ]) {
    await withPublisher({ reports: failingReports(), currentPull }, state => {
      assert.deepEqual(state.writes, []);
    });
  }
});

test('does not overwrite newer runs or newer attempts of the same run', async () => {
  for (const metadata of ['run:124 attempt:1', 'run:123 attempt:2']) {
    await withPublisher({ reports: failingReports(), comments: [botComment(7, metadata)] }, state => {
      assert.deepEqual(state.writes, []);
    });
  }
});

test('does not publish an older failure after a newer clean run that created no comment', async () => {
  const newer = { ...run, id: 124 };
  const workflowRuns = [newer, run];
  await withPublisher({ run: newer, workflowRuns }, state => {
    assert.deepEqual(state.writes, []);
  });
  await withPublisher({ reports: failingReports(), workflowRuns }, state => {
    assert.deepEqual(state.writes, []);
    assert.ok(state.logs.length > 0);
  });
});

test('skips an old attempt even when the newer attempt has not posted a comment', async () => {
  for (const status of ['queued', 'in_progress', 'completed']) {
    await withPublisher({
      reports: failingReports(),
      workflowRuns: [{ ...run, run_attempt: 2, status }],
    }, state => assert.deepEqual(state.writes, []));
  }
});

test('does not overwrite an existing warning when newer workflow metadata exists', async () => {
  await withPublisher({
    comments: [botComment()],
    workflowRuns: [{ ...run, id: 124 }, run],
  }, state => assert.deepEqual(state.writes, []));
});

test('ignores newer runs belonging to other PRs, commits, source branches, or forks', async () => {
  const unrelated = [
    { pull_requests: [{ number: 570 }] },
    { head_sha: 'b'.repeat(40) },
    { head_branch: 'other' },
    { head_repository: { full_name: 'other/a2a-dotnet' } },
  ].map(override => ({ ...run, id: 124, ...override }));
  await withPublisher({
    reports: failingReports(),
    workflowRuns: [...unrelated, run],
  }, state => assert.equal(state.writes.length, 1));
});

test('skips publication when workflow metadata cannot confirm the current run', async () => {
  await withPublisher({
    reports: failingReports(),
    workflowRuns: [],
  }, state => assert.deepEqual(state.writes, []));
});

test('updates the same comment for a newer attempt', async () => {
  await withPublisher({
    reports: failingReports(),
    run: { ...run, run_attempt: 2 },
    comments: [botComment(7, 'run:123 attempt:1')],
  }, state => {
    assert.equal(state.writes.length, 1);
    assert.equal(state.writes[0].operation, 'update');
    assert.match(state.writes[0].body, /run:123 attempt:2/);
  });
});

test('skips runs with no matching open PR', async () => {
  await withPublisher({ reports: failingReports(), pulls: [] }, state => {
    assert.deepEqual(state.writes, []);
  });
});

test('rejects non-PR or unexpected workflow sources without posting', async () => {
  for (const override of [{ event: 'push' }, { path: '.github/workflows/other.yaml' }]) {
    await withPublisher({ reports: failingReports(), run: { ...run, ...override } }, state => {
      assert.deepEqual(state.writes, []);
    });
  }
});

test('rejects missing or extra reports instead of posting an all-clear', async () => {
  for (const input of [[], [...reports(), report('extra')]]) {
    await assert.rejects(withPublisher({ reports: input }, () => {}), /report|transport/i);
  }
});
