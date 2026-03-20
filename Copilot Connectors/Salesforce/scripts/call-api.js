const http = require("http");
const endpoint = process.argv[2] || "/api/setup";
const port = process.argv[3] || "7081";

const req = http.request(
  { hostname: "localhost", port: parseInt(port), path: endpoint, method: "POST" },
  (res) => {
    let body = "";
    res.on("data", (chunk) => (body += chunk));
    res.on("end", () => {
      console.log("Status:", res.statusCode);
      try {
        console.log(JSON.stringify(JSON.parse(body), null, 2));
      } catch {
        console.log(body);
      }
    });
  }
);
req.on("error", (e) => console.log("Error:", e.message));
req.end();
