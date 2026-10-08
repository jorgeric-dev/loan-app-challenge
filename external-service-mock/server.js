// Mock of the external customer service: plain Node.js, no dependencies, data kept in memory.
//
//   POST /customers          create a customer
//   PUT  /customers/:id      update a customer (404 if it was never created)
//   GET  /customers          list everything received so far
//   POST /admin/fail?count=N make the next N create/update calls fail with 500 (to see the backend retry)
//
// Create and update must send an "Idempotency-Key" header. If the same key arrives twice
// (a retried delivery), the first answer is returned again and nothing is changed.

const http = require("node:http");

const PORT = Number(process.env.PORT ?? 4000);

const customers = new Map(); // customer id -> latest data received
const processedKeys = new Map(); // idempotency key -> response already sent
let callsToFail = Number(process.env.FAIL_REQUESTS ?? 0);

// ---------------------------------------------------------------- endpoints

function listCustomers() {
  return { status: 200, body: [...customers.values()] };
}

function createCustomer(payload) {
  const id = payload.customer.id;
  saveCustomer(id, payload, { isUpdate: false });
  log(`customer created: ${id}`);
  return { status: 200, body: { id } };
}

function updateCustomer(id, payload) {
  if (id !== payload.customer.id) return { status: 400, body: { error: "URL id and customer.id differ" } };
  if (!customers.has(id)) return { status: 404, body: { error: "Customer not found" } };

  saveCustomer(id, payload, { isUpdate: true });
  log(`customer updated: ${id}`);
  return { status: 200, body: { id } };
}

function failNextCalls(count) {
  callsToFail = count;
  log(`the next ${count} create/update call(s) will fail with 500`);
  return { status: 200, body: { callsToFail } };
}

// ---------------------------------------------------------------- routing

async function handle(request) {
  const url = new URL(request.url, "http://localhost");
  const [, resource, id] = url.pathname.split("/"); // "/customers/123" -> ["", "customers", "123"]

  if (request.method === "POST" && url.pathname === "/admin/fail") {
    return failNextCalls(Number(url.searchParams.get("count") ?? 1));
  }
  if (resource !== "customers") return notFound();
  if (request.method === "GET" && !id) return listCustomers();

  const isCreate = request.method === "POST" && !id;
  const isUpdate = request.method === "PUT" && id;
  if (!isCreate && !isUpdate) return notFound();

  // From here on: a create or an update sent by the backend.
  const key = request.headers["idempotency-key"];
  log(`${request.method} ${url.pathname}  idempotency-key=${key}  traceparent=${request.headers.traceparent ?? "-"}`);

  if (callsToFail > 0) {
    callsToFail--;
    log("simulated failure (500)");
    return { status: 500, body: { error: "Simulated failure" } };
  }
  if (!key) return { status: 400, body: { error: "Idempotency-Key header is required" } };
  if (processedKeys.has(key)) {
    log(`already processed ${key}, returning the previous answer`);
    return processedKeys.get(key);
  }

  const payload = await readJson(request);
  if (!payload?.customer?.id || !payload.application) {
    return { status: 400, body: { error: "Body must contain customer (with id) and application" } };
  }

  const response = isCreate ? createCustomer(payload) : updateCustomer(id, payload);
  if (response.status === 200) processedKeys.set(key, response);
  return response;
}

// ---------------------------------------------------------------- helpers

function saveCustomer(id, payload, { isUpdate }) {
  const previous = customers.get(id);
  const now = new Date().toISOString();
  customers.set(id, {
    ...payload,
    createdAt: previous?.createdAt ?? now,
    updatedAt: now,
    timesUpdated: (previous?.timesUpdated ?? 0) + (isUpdate ? 1 : 0),
  });
  console.log(JSON.stringify(payload, null, 2));
}

function readJson(request) {
  return new Promise((resolve) => {
    let text = "";
    request.on("data", (chunk) => (text += chunk));
    request.on("end", () => {
      try {
        resolve(JSON.parse(text));
      } catch {
        resolve(null); // invalid JSON is reported as a 400 by the caller
      }
    });
  });
}

function notFound() {
  return { status: 404, body: { error: "Not found" } };
}

function log(message) {
  console.log(`[${new Date().toISOString()}] ${message}`);
}

// ---------------------------------------------------------------- server

http
  .createServer(async (request, response) => {
    const { status, body } = await handle(request);
    response.writeHead(status, { "Content-Type": "application/json" });
    response.end(JSON.stringify(body, null, 2));
  })
  .listen(PORT, () => log(`External service mock listening on http://localhost:${PORT}`));
