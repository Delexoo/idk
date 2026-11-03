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
  if (body.portal === true) {
    const found = await findActive(secret, device, "");
    if (!found?.customerId) throw new Error("This computer does not have an active subscription.");
    return { url: await portalUrl(secret, found.customerId) };
  }
  const sessionId = typeof body.sessionId === "string" ? body.sessionId.trim() : "";
  return (await findActive(secret, device, sessionId)) || emptyLicense();
}

function emptyLicense() {
  return { active: false, email: "", sessionId: "", subscriptionId: "", periodEnd: 0, customerId: "" };
}

async function findActive(secret, device, sessionId) {
  if (sessionId) {
    if (!/^cs_[A-Za-z0-9_]+$/.test(sessionId)) throw new Error("Unknown checkout.");
    const payload = await stripe(secret, "GET", `/v1/checkout/sessions/${sessionId}`);
    return paidForDevice(secret, payload, device);
  }
  try {
    const params = new URLSearchParams();
    params.set("query", `client_reference_id:'${device}' AND status:'complete' AND mode:'subscription'`);
    params.set("limit", "5");
    const payload = await stripe(secret, "GET", `/v1/checkout/sessions/search?${params}`);
    const rows = Array.isArray(payload.data) ? payload.data : [];
    for (const row of rows) {
      const paid = await paidForDevice(secret, row, device);
      if (paid) return paid;
    }
    return null;
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
      return null;
    }
    const rows = Array.isArray(payload.data) ? payload.data : [];
    for (const row of rows) {
      const paid = await paidForDevice(secret, row, device);
      if (paid) return paid;
    }
    const last = rows.at(-1);
    if (payload.has_more !== true || !last || typeof last.id !== "string") {
      return null;
    }
    startingAfter = last.id;
  }
  return null;
}

async function paidForDevice(secret, record, device) {
  if (!record || typeof record !== "object") return null;
  if (record.mode !== "subscription") return null;
  const reference = typeof record.client_reference_id === "string" ? record.client_reference_id : "";
  const metadata = record.metadata && typeof record.metadata === "object" ? record.metadata : {};
  const tagged = typeof metadata.device === "string" ? metadata.device : "";
  if (reference !== device && tagged !== device) return null;
  const paid = record.status === "complete" && (record.payment_status === "paid" || record.payment_status === "no_payment_required");
  const sessionId = typeof record.id === "string" ? record.id : "";
  const subscriptionId = typeof record.subscription === "string" ? record.subscription : "";
  if (!paid || !/^cs_[A-Za-z0-9_]+$/.test(sessionId) || !/^sub_[A-Za-z0-9]+$/.test(subscriptionId)) return null;
  const sub = await stripe(secret, "GET", `/v1/subscriptions/${subscriptionId}`);
  const subStatus = typeof sub.status === "string" ? sub.status : "";
  if (subStatus !== "active" && subStatus !== "trialing") return null;
  const subMeta = sub.metadata && typeof sub.metadata === "object" ? sub.metadata : {};
  const subDevice = typeof subMeta.device === "string" ? subMeta.device : "";
  if (subDevice && subDevice !== device) return null;
  const details = record.customer_details && typeof record.customer_details === "object" ? record.customer_details : {};
  const email = typeof record.customer_email === "string" && record.customer_email
    ? record.customer_email
    : (typeof details.email === "string" ? details.email : "");
  const customerId = typeof sub.customer === "string" ? sub.customer : "";
  return { active: true, email, sessionId, subscriptionId, periodEnd: periodEndOf(sub), customerId };
}

function periodEndOf(sub) {
  if (typeof sub.current_period_end === "number") return sub.current_period_end;
  const items = sub.items && Array.isArray(sub.items.data) ? sub.items.data : [];
  const first = items[0];
  return first && typeof first.current_period_end === "number" ? first.current_period_end : 0;
}

async function portalUrl(secret, customerId) {
  const origin = (process.env.IDK_BILLING_ORIGIN || "").replace(/\/$/, "");
  const params = new URLSearchParams();
  params.set("customer", customerId);
  params.set("return_url", origin.startsWith("https://") ? origin : "https://stripe.com");
  const payload = await stripe(secret, "POST", "/v1/billing_portal/sessions", params);
  if (typeof payload.url !== "string" || !payload.url.startsWith("https://")) {
    throw new Error("Billing could not open.");
  }
  return payload.url;
}

async function stripe(secret, method, path, body) {
  const response = await fetch(`https://api.stripe.com${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${secret}`,
      ...(body ? { "content-type": "application/x-www-form-urlencoded" } : {}),
    },
    body: body ? body.toString() : undefined,
  });
  const payload = await response.json();
  if (!response.ok) {
    const message = payload.error && payload.error.message ? payload.error.message : "Billing could not be checked.";
    throw new Error(String(message).slice(0, 180));
  }
  return payload;
}
