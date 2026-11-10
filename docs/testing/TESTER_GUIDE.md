# Test the app from Cursor

You can ask Cursor, in ordinary sentences, to try a feature in the browser and save that try as a test. The tests run on a throwaway copy of the app on your computer. They do not use the live system, and they do not use the database you open for everyday development.

The throwaway app is `http://127.0.0.1:3200`. The test accounts are `hr@example.test`, `amirul@example.test`, `admin@example.test`, `sarah@example.test`, and `daniel@example.test`. The password is `e2e-only-password`.

## Set up once

1. Open Docker Desktop and wait until it is running.
2. In the project folder, run `npm install`.
3. In the project folder, run `npx playwright install chromium`.
4. Open Cursor Settings, then MCP. Enable the Playwright server. This project already lists it in `.cursor/mcp.json`.

Playwright is the browser tool Cursor uses for these tests. MCP is the Cursor settings page where that tool is switched on.

## Ask Cursor to test

Paste one sentence. Wait for the report before you paste the next request.

```text
Test the leave approval feature.
```

```text
Check that employees cannot see HR approvals.
```

```text
Write UI tests for expense claims.
```

```text
Run the UI tests.
```

```text
Why did the UI test fail?
```

```text
The button now says Submit claim. Update the UI tests.
```

For the last kind of request, say what the old words were and what the new words are. Cursor should update a test only after the wording change was intended.

To watch the throwaway app yourself, run `npm run e2e:env` and wait until it prints the app address. Sign in with one of the accounts above. When you are finished, run `npm run e2e:stop`.

## Read the report

After a run, ask Cursor to open the report, or run `npm run test:e2e:report` in the project folder.

- A passed test did what the business rule says.
- A failed test is either a broken test or a broken app. The report from Cursor says which.
- An expected fail is a known bug in the app. The test still describes the correct behavior. The app does not do that yet.

Open a test in the report to play its video. Open the trace on that same test to see each click. The video, the screenshot, and the trace files are also in the `test-results` folder.

## What an expected fail means

An expected fail is labeled as an app bug. The screen, or the saved row, disagrees with the company rule. The test was not changed to accept the wrong result.

When a developer fixes the app, that test should pass. Cursor then removes the expected-fail mark. Until then, treat the item as a product bug, not as a bad test.

## When something goes wrong

If the message says Docker Desktop is not running, open Docker Desktop and wait until it is running. Then run the tests again.

If the message says port 3200 is already in use, a previous test app may still be running. Run `npm run e2e:stop`. Then start the test again.

If the tests say they only run against this computer, they stopped on purpose. They also stop on purpose when sign-in did not reach the throwaway database. They did not change your data. Run `npm run e2e:stop`, then run the tests again. Do not change the address in `playwright.config.ts`.

## What not to do

Do not ask Cursor to run these tests against the live site, against `http://localhost:3000`, or against port 3100.

Do not put your everyday database into the tests.

Do not delete the safety check, and do not edit the app so that a failing test turns green.
