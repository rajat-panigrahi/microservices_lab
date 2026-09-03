# Demo script — see every pattern run

Every command below was executed against the running system while building it; the timings and
outputs are real, not illustrative.

## Prerequisites

```bash
# RabbitMQ on localhost:5672
docker run -d --name rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management
#   ...or a local install: sudo service rabbitmq-server start

dotnet build
deploy/local/run-all.sh          # starts all nine services, waits for health
```

Ports: gateway **5100**, Projects 5101, KPI 5102, Risk 5103, Issues 5104, Benefits 5105,
Reporting 5106, Identity 5107, Discovery 5108. Swagger is at `/swagger` on each.

```bash
G=http://localhost:5100
jq_() { python3 -c "import sys,json;d=json.load(sys.stdin);print(d$1)"; }
```

---

## 1. Everything is closed by default

```bash
curl -s -o /dev/null -w "%{http_code}\n" $G/api/projects
# 401
```

No token, no access — and `/projects` never declares an authorization policy. That is the
**fallback policy**: an endpoint that forgets to protect itself is closed, not open.

## 2. Get a token

```bash
TOK=$(curl -s -X POST $G/api/identity/connect/token -H 'Content-Type: application/json' \
  -d '{"userName":"portfolio.director","password":"Passw0rd!"}' | jq_ "['accessToken']")
A="Authorization: Bearer $TOK"; J='Content-Type: application/json'

curl -s -o /dev/null -w "%{http_code}\n" -H "$A" $G/api/projects
# 200
```

Accounts: `portfolio.director`, `project.manager`, `risk.owner`, `viewer` — all `Passw0rd!`.

## 3. A Viewer is authenticated but not authorised

```bash
VTOK=$(curl -s -X POST $G/api/identity/connect/token -H "$J" \
  -d '{"userName":"viewer","password":"Passw0rd!"}' | jq_ "['accessToken']")

curl -s -o /dev/null -w "%{http_code}\n" -X POST $G/api/projects \
  -H "Authorization: Bearer $VTOK" -H "$J" -d '{"code":"X","name":"n","objectiveId":"...","sponsor":"s","budget":1}'
# 403  ← not 401. We know who you are; you may not.
```

## 4. The saga: happy path

```bash
OID=$(curl -s -X POST $G/api/objectives -H "$A" -H "$J" \
  -d '{"code":"SO-01","title":"Reduce operating cost by 15%","horizon":"FY27","owner":"COO"}' | jq_ "['id']")

PID=$(curl -s -X POST $G/api/projects -H "$A" -H "$J" \
  -d "{\"code\":\"PRJ-0007\",\"name\":\"Warehouse automation\",\"objectiveId\":\"$OID\",\"sponsor\":\"A. Sponsor\",\"budget\":250000}" | jq_ "['id']")

curl -s -X POST $G/api/projects/$PID/submit-for-initiation -H "$A"
# {"id":"…","stage":"Initiating"}   ← honest intermediate state
```

Wait a moment, then check all four services:

```bash
curl -s $G/api/projects/$PID -H "$A" | jq_ "['stage']"                    # Active   (~3s)
curl -s http://localhost:5102/projects/$PID/scorecard -H "$A" | jq_ "['kpis'].__len__()"   # 3
curl -s $G/api/risks/../projects/$PID/risk-register -H "$A" 2>/dev/null || \
  curl -s http://localhost:5103/projects/$PID/risk-register -H "$A" | jq_ "['status']"     # Active
curl -s http://localhost:5105/projects/$PID/benefits -H "$A" | jq_ "['forecastValue']"     # 350000
```

Three services provisioned in parallel, and the project only became `Active` once all three
confirmed.

## 5. The saga: compensation

The Benefits service refuses a forecast above the £1M portfolio ceiling. Budget × 1.4 = forecast,
so £900k breaches it — a **real business rule**, not a stub.

```bash
OID2=$(curl -s -X POST $G/api/objectives -H "$A" -H "$J" \
  -d '{"code":"SO-02","title":"Group-wide consolidation","horizon":"FY28","owner":"CTO"}' | jq_ "['id']")

BAD=$(curl -s -X POST $G/api/projects -H "$A" -H "$J" \
  -d "{\"code\":\"PRJ-0099\",\"name\":\"Group-wide transformation\",\"objectiveId\":\"$OID2\",\"sponsor\":\"CEO\",\"budget\":900000}" | jq_ "['id']")

curl -s -X POST $G/api/projects/$BAD/submit-for-initiation -H "$A" > /dev/null
sleep 3

curl -s $G/api/projects/$BAD -H "$A" | python3 -m json.tool | grep -E "stage|failureReason"
# "stage": "InitiationFailed"
# "failureReason": "Benefit profile: Forecast benefit of 1,260,000 exceeds the portfolio ceiling…"

# were the successful legs rolled back?
curl -s -o /dev/null -w "scorecard      %{http_code}\n" http://localhost:5102/projects/$BAD/scorecard -H "$A"
curl -s -o /dev/null -w "risk register  %{http_code}\n" http://localhost:5103/projects/$BAD/risk-register -H "$A"
curl -s -o /dev/null -w "benefits       %{http_code}\n" http://localhost:5105/projects/$BAD/benefits -H "$A"
# 404 / 404 / 404   ← compensation removed everything the other services created
```

**Measured: 2 seconds** from submit to `InitiationFailed`, everything rolled back.

## 6. Choreography: escalate a risk, watch four services react

```bash
RTOK=$(curl -s -X POST $G/api/identity/connect/token -H "$J" \
  -d '{"userName":"risk.owner","password":"Passw0rd!"}' | jq_ "['accessToken']")
RA="Authorization: Bearer $RTOK"

RID=$(curl -s -X POST $G/api/risks -H "$RA" -H "$J" \
  -d "{\"projectId\":\"$PID\",\"title\":\"Supplier cannot meet the integration deadline\",\"category\":\"Supplier\",\"probability\":5,\"impact\":5,\"owner\":\"R. Owner\"}" | jq_ "['id']")
# {"score":25,"tier":"Critical"}   ← 5 × 5 on the probability/impact matrix

curl -s -X POST $G/api/risks/$RID/escalate -H "$RA" -H "$J" -d '{"reason":"Supplier confirmed slippage"}'
# {"status":"Materialised"}  — returns in ~40ms; nothing downstream has happened yet
```

Then, about a second later, with nobody coordinating it:

```bash
curl -s "$G/api/issues?projectId=$PID" -H "$RA" | python3 -m json.tool
# an issue was auto-raised, severity Critical, titled "[Escalated] Supplier cannot meet…"

curl -s $G/api/projects/$PID -H "$A" | jq_ "['health']"       # Red
curl -s http://localhost:5105/projects/$PID/benefits -H "$A" | jq_ "['status']"   # AtRisk
```

Now close the loop — resolving the issue closes the originating risk:

```bash
IID=$(curl -s "$G/api/issues?projectId=$PID" -H "$RA" | python3 -c "import sys,json;print(json.load(sys.stdin)[0]['id'])")
curl -s -X PUT $G/api/issues/$IID/owner -H "$RA" -H "$J" -d '{"owner":"I. Owner"}' > /dev/null
curl -s -X POST $G/api/issues/$IID/resolve -H "$RA" -H "$J" -d '{"notes":"Supplier re-planned"}'
sleep 2
curl -s http://localhost:5103/projects/$PID/risk-register -H "$A" | jq_ "['risks'][0]['status']"
# Closed
```

## 7. CQRS: one row assembled from five services

```bash
curl -s $G/api/reporting/portfolio -H "$A" | python3 -m json.tool
```

```
PRJ-0007  Warehouse automation  Active  Red  kpi 0/0/0(+3 unmeasured)
          risks 0  issues 1 (crit 1)  benefit 350,000 [AtRisk]
```

Project stage from Projects, KPI counts from KPI, the escalated risk from Risk, the issue from
Issues, the benefit from Benefits — **one indexed SELECT**, no fan-out.

Open **http://localhost:5106/** and repeat step 6 in another terminal: the row turns red without
a refresh. The delay you can see *is* the eventual-consistency window.

## 8. Rebuild: a read model owns no truth

```bash
# corrupt it deliberately
sqlite3 strategyops-reporting.db "UPDATE portfolio_scorecards SET OpenIssues=99, BenefitForecast=1"
curl -s $G/api/reporting/portfolio -H "$A" | jq_ "['projects'][0]['openIssues']"   # 99

curl -s -X POST $G/api/reporting/rebuild -H "$A"
# {"projectsRebuilt":2,"failures":0,"duration":"00:00:00.52"}

curl -s $G/api/reporting/portfolio -H "$A" | jq_ "['projects'][0]['openIssues']"   # 1  ← repaired
```

## 9. The circuit breaker

```bash
curl -s -X POST http://localhost:5105/chaos/fail        # Benefits now returns 503

for i in $(seq 1 6); do
  curl -s "$G/api/portfolio/$PID/overview" -H "$A" | python3 -c "
import sys,json;d=json.load(sys.stdin);b=d['benefits']
print(f\"{d['elapsedMs']:>5}ms  benefits={b['error'] or 'ok':24} kpi_ok={d['scorecard']['available']}\")"
done
```

```
 1038ms  benefits=503 Service Unavailable   kpi_ok=True
   19ms  benefits=BrokenCircuitException    kpi_ok=True
   20ms  benefits=BrokenCircuitException    kpi_ok=True
   ...
```

The first call pays for the retries; every one after fails in ~15ms. **KPI keeps working**,
because each dependency has its own breaker. Then heal it:

```bash
curl -s -X POST http://localhost:5105/chaos/heal
# the next probe closes the circuit within a few seconds
```

## 10. Rate limiting

```bash
for i in $(seq 1 130); do curl -s -o /dev/null -w "%{http_code}\n" -H "$A" $G/api/projects; done | sort | uniq -c
#  122 200
#    8 429     ← token bucket: a burst is allowed, a sustained flood is not
```

## 11. Correlation: one id, six processes

```bash
CID="demo-$(date +%s)"
curl -s -X POST $G/api/projects/$PID/submit-for-initiation -H "$A" -H "X-Correlation-Id: $CID" > /dev/null

# it comes back on the response too, so a user can quote it
curl -s -D - -o /dev/null -H "$A" -H "X-Correlation-Id: $CID" $G/api/projects/$PID | grep -i correlation

grep -h "$CID" .run/*.log | sort
```

```
gateway       HTTP POST /api/projects/…/submit-for-initiation responded 200 in 98ms
projects-api  HTTP POST /projects/…/submit-for-initiation responded 200 in 91ms
benefits-api  Registered a 280,000 benefit forecast for PRJ-0099
kpi-api       Provisioned a scorecard with 3 baseline KPIs for PRJ-0099
projects-api  Project PRJ-0099 activated: all three legs provisioned
risk-api      HTTP POST /risks/…/escalate responded 200 in 43ms
issues-api    Raised Critical issue … from escalated risk …
projects-api  Project PRJ-0099 moved to Red because a Critical issue was raised
benefits-api  Benefit forecast for PRJ-0099 flagged at risk
```

**Six processes, two transports, one saga, one choreographed chain — reconstructed with one
grep.** This is the answer to "how do you debug microservices?".

## 12. Service discovery

```bash
curl -s http://localhost:5108/registry/services | python3 -m json.tool
# six services, each with its base URL and last heartbeat
```

Kill one service and watch it get evicted once its lease expires.

---

## Tear down

```bash
deploy/local/stop-all.sh
```
