export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.status(405).json({ error: "Method not allowed." });
    return;
  }
  try {
    const url = await startCheckout(req);
    res.status(200).json({ url });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Billing could not start.";
    res.status(400).json({ error: message.slice(0, 180) });
  }
}

async function startCheckout(req) {
  const secret = process.env.STRIPE_SECRET_KEY || "";
  if (!secret) throw new Error("Billing is not configured yet.");
  const body = req.body && typeof req.body === "object" ? req.body : {};
  const email = typeof body.email === "string" ? body.email.trim() : "";
  if (email && (email.length > 200 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))) {
    throw new Error("Enter a valid email address.");
  }
  const device = typeof body.device === "string" ? body.device.trim() : "";
  if (!device || !/^[a-f0-9]{64}$/.test(device)) throw new Error("This computer could not be identified.");
  const successUrl = returnUrl(body.successUrl);
  const cancelUrl = returnUrl(body.cancelUrl);
  const params = new URLSearchParams();
  params.set("mode", "subscription");
  params.set("line_items[0][quantity]", "1");
  params.set("line_items[0][price_data][currency]", "usd");
  params.set("line_items[0][price_data][unit_amount]", "300");
  params.set("line_items[0][price_data][recurring][interval]", "month");
  params.set("line_items[0][price_data][product_data][name]", "idk Pro");
  params.set("line_items[0][price_data][product_data][description]", "$3 a month for this computer.");
  params.set("success_url", successUrl);
  params.set("cancel_url", cancelUrl);
  params.set("metadata[device]", device);
  params.set("subscription_data[metadata][device]", device);
  params.set("client_reference_id", device);
  if (email) params.set("customer_email", email);
  params.set("allow_promotion_codes", "true");
  const response = await fetch("https://api.stripe.com/v1/checkout/sessions", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${secret}`,
      "content-type": "application/x-www-form-urlencoded",
    },
    body: params.toString().replaceAll("%7BCHECKOUT_SESSION_ID%7D", "{CHECKOUT_SESSION_ID}"),
  });
  const payload = await response.json();
  if (!response.ok || typeof payload.url !== "string") {
    const message = payload.error && payload.error.message ? payload.error.message : "Billing could not start.";
    throw new Error(String(message).slice(0, 180));
  }
  return payload.url;
}

function returnUrl(value) {
  if (typeof value !== "string") throw new Error("Billing could not start.");
  let url;
  try {
    url = new URL(value.replace("{CHECKOUT_SESSION_ID}", "cs_test_placeholder"));
  } catch {
    throw new Error("Billing could not start.");
  }
  const local = url.protocol === "http:" && url.hostname === "127.0.0.1";
  const pathOk = url.pathname === "/paid" || url.pathname === "/cancel";
  if (!local || !pathOk) throw new Error("Billing could not start.");
  return value;
}
