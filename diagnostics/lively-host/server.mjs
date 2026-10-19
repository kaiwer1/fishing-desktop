// Isolated host probe; never imports game code or reads browser profiles/saves.
import http from 'node:http';
import { readFile, appendFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const port = 43817;
const recordPath = process.env.PROBE_RECORD;
const session = randomUUID();
const events = [];
const page = await readFile(new URL('./index.html', import.meta.url));
const server = http.createServer(async (req, res) => {
  // Also accepts a local Lively virtual-host page, using CORS without credentials.
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Private-Network', 'true');
  res.setHeader('Cache-Control', 'no-store');
  const url = new URL(req.url, `http://127.0.0.1:${port}`);
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.writeHead(204).end();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/') {
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.end(page);
    return;
  }
  if (req.method === 'GET' && url.pathname === '/status') {
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify({ session, events }));
    return;
  }
  if (req.method === 'POST' && url.pathname === '/event') {
    let raw = '';
    try {
      for await (const chunk of req) {
        raw += chunk;
        if (Buffer.byteLength(raw) > 8192) {
          res.writeHead(413).end();
          return;
        }
      }
      const data = JSON.parse(raw);
      const allowed = ['boot', 'heartbeat', 'pause', 'property', 'error', 'visibility', 'pointer', 'key'];
      if (!allowed.includes(data.kind)) throw new Error('Unknown event');
      const entry = { receivedAt: new Date().toISOString(), ...data };
      events.push(entry);
      if (events.length > 600) events.shift();
      if (recordPath) await appendFile(recordPath, JSON.stringify(entry) + '\n');
      res.writeHead(204).end();
    } catch {
      res.writeHead(400).end();
    }
    return;
  }
  res.writeHead(404).end();
});
server.listen(port, '127.0.0.1', () => {
  console.log(JSON.stringify({ probe: fileURLToPath(import.meta.url), url: `http://127.0.0.1:${port}/`, session }));
});
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => server.close());
