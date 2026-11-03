export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.status(405).json({ error: "Method not allowed." });
    return;
  }
  try {
    const result = await lookupLicense(req);
    res.status(200).json(result);
  } catch (error) {
    const message = error instanceof Error ? error.message : "Billing could not be checked.";
    res.status(400).json({ error: message.slice(0, 180) });
  }
}

async function lookupLicense(req) {
  const secret = process.env.STRIPE_SECRET_KEY || "";
  if (!secret) throw new Error("Billing is not configured yet.");
  const body = req.body && typeof req.body === "object" ? req.body : {};
  const device = typeof body.device === "string" ? body.device.trim() : "";
  if (!/^[a-f0-9]{64}$/.test(device)) throw new Error("This computer could not be identified.");
  const sessionId = typeof body.sessionId === "string" ? body.sessionId.trim() : "";
  if (sessionId) {
    if (!/^cs_[A-Za-z0-9_]+$/.test(sessionId)) throw new Error("Unknown checkout.");
    const payload = await stripe(secret, "GET", `/v1/checkout/sessions/${sessionId}`);
    return paidForDevice(payload, device) || { active: false, email: "", sessionId: "" };
  }
  try {
    const params = new URLSearchParams();
    params.set("query", `client_reference_id:'${device}' AND status:'complete'`);
    params.set("limit", "5");
    const payload = await stripe(secret, "GET", `/v1/checkout/sessions/search?${params}`);
    const rows = Array.isArray(payload.data) ? payload.data : [];
    for (const row of rows) {
      const paid = paidForDevice(row, device);
      if (paid) return paid;
    }
    return { active: false, email: "", sessionId: "" };
  } catch {
    return findInList(secret, device);
  }
}

async function findInList(secret, device) {
  let startingAfter = "";
  for (let page = 0; page < 10; page += 1) {
    const params = new URLSearchParams();
    params.set("limit", "100");
    params.set("status", "complete");
    if (startingAfter) params.set("starting_after", startingAfter);
    let payload;
    try {
      payload = await stripe(secret, "GET", `/v1/checkout/sessions?${params}`);
    } catch {
      return { active: false, email: "", sessionId: "" };
    }
    const rows = Array.isArray(payload.data) ? payload.data : [];
    for (const row of rows) {
      const paid = paidForDevice(row, device);
      if (paid) return paid;
    }
    const last = rows.at(-1);
    if (payload.has_more !== true || !last || typeof last.id !== "string") {
      return { active: false, email: "", sessionId: "" };
    }
    startingAfter = last.id;
  }
  return { active: false, email: "", sessionId: "" };
}

function paidForDevice(record, device) {
  if (!record || typeof record !== "object") return null;
  const reference = typeof record.client_reference_id === "string" ? record.client_reference_id : "";
  const metadata = record.metadata && typeof record.metadata === "object" ? record.metadata : {};
  const tagged = typeof metadata.device === "string" ? metadata.device : "";
  if (reference !== device && tagged !== device) return null;
  const paid = record.status === "complete" && (record.payment_status === "paid" || record.payment_status === "no_payment_required");
  const sessionId = typeof record.id === "string" ? record.id : "";
  if (!paid || !/^cs_[A-Za-z0-9_]+$/.test(sessionId)) return null;
  const details = record.customer_details && typeof record.customer_details === "object" ? record.customer_details : {};
  const email = typeof record.customer_email === "string" && record.customer_email
    ? record.customer_email
    : (typeof details.email === "string" ? details.email : "");
  return { active: true, email, sessionId };
}

async function stripe(secret, method, path) {
  const response = await fetch(`https://api.stripe.com${path}`, {
    method,
    headers: { Authorization: `Bearer ${secret}` },
  });
  const payload = await response.json();
  if (!response.ok) {
    const message = payload.error && payload.error.message ? payload.error.message : "Billing could not be checked.";
    throw new Error(String(message).slice(0, 180));
  }
  return payload;
}
