const { lstatSync, readFileSync, readdirSync } = require('node:fs');
const { join } = require('node:path');

const marker = '<!-- a2a-acts-advisories -->';
const transports = ['grpc', 'jsonrpc', 'rest'];

function cell(value, limit = 400) {
  return String(value).slice(0, limit).replace(/\s+/g, ' ')
    .replace(/[&<>|`\\[\]*_~@]/g, character => `&#${character.charCodeAt(0)};`);
}

function buildComment(reports, run) {
  if (reports.length !== transports.length ||
      new Set(reports.map(report => report.transport)).size !== transports.length ||
      reports.some(report => !transports.includes(report.transport))) {
    throw new Error('Expected exactly one ACTS report for each transport: grpc, jsonrpc, rest.');
  }
  const failures = [];
  for (const report of reports) {
    if (report.sdk?.name !== 'a2a-dotnet' || !Array.isArray(report.suites) || report.suites.length === 0) {
      throw new Error('Invalid ACTS report SDK or suites.');
    }
    const tests = report.suites.flatMap(suite => {
      if (!Array.isArray(suite.tests)) {
        throw new Error('Invalid ACTS suite tests.');
      }
      return suite.tests;
    });
    if (tests.length === 0 || report.summary?.total !== tests.length) {
      throw new Error('Invalid ACTS report test total.');
    }
    const ids = new Set();
    for (const test of tests) {
      if (typeof test.id !== 'string' || !test.id || ids.has(test.id) ||
          !['must', 'should', 'may'].includes(test.level) ||
          !['pass', 'fail', 'error', 'skip'].includes(test.result)) {
        throw new Error('Invalid or duplicate ACTS test.');
      }
      ids.add(test.id);
    }
    for (const level of ['should', 'may']) {
      const selected = tests.filter(test => test.level === level);
      const counts = report.summary.by_level?.[level];
      if (!counts || counts.failed !== selected.filter(test => test.result === 'fail').length ||
          counts.errors !== selected.filter(test => test.result === 'error').length) {
        throw new Error(`Invalid ACTS ${level} summary.`);
      }
      for (const test of selected.filter(test => ['fail', 'error'].includes(test.result))) {
        failures.push({ ...test, transport: report.transport });
      }
    }
  }

  if (!Number.isSafeInteger(run.id) || run.id <= 0 ||
      !Number.isSafeInteger(run.run_attempt) || run.run_attempt <= 0 ||
      !/^[a-f0-9]{40}$/.test(run.head_sha)) {
    throw new Error('Invalid ACTS workflow run metadata.');
  }
  const url = `https://github.com/${run.repository.full_name}/actions/runs/${run.id}/attempts/${run.run_attempt}`;
  const lines = [
    marker,
    `<!-- run:${run.id} attempt:${run.run_attempt} -->`,
    '## ACTS advisory results',
    '',
    `Commit: \`${run.head_sha.slice(0, 12)}\` | [ACTS run and full reports](${url})`,
    '',
    '**Non-blocking advisory:** This comment is informational only and does not block merging this PR. Advisory failures may or may not be related to changes in this PR; they do not, on their own, establish a regression introduced by this PR. The MUST-only conformance gate is unchanged.',
    '',
  ];
  if (failures.length === 0) {
    lines.push('No SHOULD or MAY failures or errors were reported across gRPC, JSON-RPC, and REST.');
  } else {
    lines.push(
      `**${failures.length} SHOULD/MAY failure(s) or error(s)** across the tested transports.`,
      '',
      '| Level | Transport | Test | Result | Reason |',
      '|---|---|---|---|---|',
    );
    failures.sort((left, right) => {
      const level = { should: 0, may: 1 };
      return level[left.level] - level[right.level] ||
        left.transport.localeCompare(right.transport) || left.id.localeCompare(right.id);
    });
    let shown = 0;
    let length = lines.join('\n').length;
    for (const failure of failures) {
      const row = `| ${failure.level.toUpperCase()} | ${failure.transport} | ${cell(failure.id, 100)} | ${failure.result} | ${cell(failure.failure?.message ?? 'No failure detail provided.')} |`;
      if (shown >= 100 || length + row.length > 50000) {
        break;
      }
      lines.push(row);
      length += row.length + 1;
      shown++;
    }
    if (shown < failures.length) {
      lines.push('', `${failures.length - shown} additional findings omitted; see the full reports linked above.`);
    }
  }
  return { body: lines.join('\n'), failureCount: failures.length };
}

function readReports(directory) {
  const files = readdirSync(directory).filter(name => /^acts-report-.*\.json$/.test(name));
  if (files.length !== transports.length) {
    throw new Error('Expected exactly three ACTS report files.');
  }
  return files.map(name => {
    const path = join(directory, name);
    const stat = lstatSync(path);
    if (!stat.isFile() || stat.size > 5 * 1024 * 1024) {
      throw new Error('ACTS reports must be regular JSON files no larger than 5 MiB.');
    }
    return JSON.parse(readFileSync(path, 'utf8'));
  });
}

async function publishComment({ github, context, core, reportDirectory }) {
  const run = context.payload.workflow_run;
  const { owner, repo } = context.repo;
  if (run.event !== 'pull_request' || run.name !== 'ACTS' ||
      run.path !== '.github/workflows/acts.yaml' ||
      run.repository.full_name !== `${owner}/${repo}`) {
    core.info('Skipping a run that is not from the ACTS pull-request workflow.');
    return;
  }
  // Fork workflow_run events can have an empty pull_requests array.
  const pulls = await github.paginate(github.rest.pulls.list, {
    owner, repo, state: 'open',
    head: `${run.head_repository.owner.login}:${run.head_branch}`,
    per_page: 100,
  });
  const associatedPulls = new Set(run.pull_requests.map(pull => pull.number));
  const matches = pull => pull.state === 'open' &&
    (associatedPulls.size === 0 || associatedPulls.has(pull.number)) &&
    pull.head.sha === run.head_sha &&
    pull.head.repo?.full_name === run.head_repository.full_name &&
    pull.base.repo.full_name === `${owner}/${repo}`;
  const candidates = pulls.filter(matches);
  if (candidates.length !== 1) {
    core.info('Expected exactly one matching open PR for this ACTS run and head commit.');
    return;
  }
  const [pull] = candidates;

  const result = buildComment(readReports(reportDirectory), run);
  const comments = await github.paginate(github.rest.issues.listComments, {
    owner, repo, issue_number: pull.number, per_page: 100,
  });
  const existing = comments.find(comment =>
    comment.user?.login === 'github-actions[bot]' && comment.user.type === 'Bot' &&
    comment.body?.startsWith(`${marker}\n`));
  const previous = existing?.body.match(/<!-- run:(\d+) attempt:(\d+) -->/);
  if (previous && (Number(previous[1]) > run.id ||
      (Number(previous[1]) === run.id && Number(previous[2]) > run.run_attempt))) {
    core.info('A newer ACTS result has already been posted.');
    return;
  }
  if (!existing && result.failureCount === 0) {
    core.info('No ACTS advisory findings; no new PR comment is needed.');
    return;
  }
  const current = await github.rest.pulls.get({ owner, repo, pull_number: pull.number });
  if (!matches(current.data)) {
    core.info('The PR changed or closed while the ACTS reports were being processed.');
    return;
  }
  const workflowRuns = await github.paginate(github.rest.actions.listWorkflowRuns, {
    owner, repo, workflow_id: 'acts.yaml', event: 'pull_request',
    head_sha: run.head_sha, branch: run.head_branch, per_page: 100,
  });
  const latest = workflowRuns.filter(candidate =>
    candidate.head_sha === run.head_sha &&
    candidate.head_branch === run.head_branch &&
    candidate.head_repository?.full_name === run.head_repository.full_name &&
    (candidate.pull_requests.length === 0 ||
      candidate.pull_requests.some(associated => associated.number === pull.number)))
    .sort((left, right) => right.id - left.id || right.run_attempt - left.run_attempt)[0];
  if (!latest || latest.id !== run.id || latest.run_attempt !== run.run_attempt ||
      latest.status !== 'completed') {
    core.info('This is not the latest completed ACTS run attempt for the PR and head commit.');
    return;
  }
  if (existing) {
    await github.rest.issues.updateComment({
      owner, repo, comment_id: existing.id, body: result.body,
    });
  } else {
    await github.rest.issues.createComment({
      owner, repo, issue_number: pull.number, body: result.body,
    });
  }
}

module.exports = { buildComment, publishComment };
