"""Export the audited public contract. Run from any directory; no credentials or live data."""
from pathlib import Path
import json, re, hashlib

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'api-contract'
OUT.mkdir(exist_ok=True)
schemas={}
descriptions={
 'id':'Stable identifier for this resource.', 'userId':'Owning account UUID.',
 'instrumentId':'Canonical instrument ID from GET /api/products. Use the ID, not the display symbol.',
 'quantity':'BTC quantity. Signed quantities on positions: positive long, negative short.',
 'remaining':'Unfilled BTC quantity.', 'reserved':'Funds reserved by this resource.',
 'price':'USDT price per BTC.', 'available':'Available asset amount; derivative transfer limits are in portfolio.risk.',
 'locked':'Asset amount reserved for outstanding operations.', 'kind':'Resource or wallet kind; spelling is case-sensitive.',
 'asset':'BTC or USDT.', 'status':'Current lifecycle state. See the relevant guide for transitions.',
 'createdAt':'UTC creation timestamp.', 'expiresAt':'UTC expiry timestamp.', 'expiry':'Contract expiry in UTC; null for spot/perpetual.',
 'sequence':'Monotonic database sequence represented as a string. Gaps are valid.', 'quoteValidatedAt':'Internal maker quote freshness timestamp, nullable for participant orders.',
 'cost':'Signed USDT position cost basis, used for exact realized P&L.', 'entryPrice':'Average entry price; up to 16 decimal places.',
 'equity':'Cross-margin equity in USDT after collateral haircuts and option coverage.',
 'initialMargin':'USDT initial margin required by positions and resting derivative orders.',
 'maintenanceMargin':'USDT maintenance margin required by futures positions.',
 'transferableUsdt':'USDT that can leave Unified Trading under current risk checks.',
 'transferableBtc':'BTC that can leave Unified Trading under current risk checks.',
 'realizedCash':'Signed realized USDT cash claim/obligation; a positive claim may await funded settlement.',
 'unrealizedPnl':'Mark-to-market USDT unrealized P&L, including option value.',
 'priceFresh':'Whether required price references are fresh.', 'restricted':'Whether maintenance or covered-option constraints are breached.',
 'shares':'Product units, not necessarily token amounts.', 'queuedShares':'Earn units requested for redemption, still included in shares until paid.',
 'feesEarned':'Cumulative LP share of AMM execution fees in USDT; already reflected in pool NAV.',
 'interestRemainder':'Sub-ledger interest remainder, retained at up to 20 decimals.',
 'reserveRemainder':'Sub-ledger reserve remainder retained at up to 20 decimals.',
 'total':'Cumulative BTC quantity from the best price through this level.',
 'change':'Percentage change over exchange trades in the last 24 hours; null with no trades.',
 'lastPrice':'Last executed exchange price; null when unavailable. Not the external index.',
 'nav':'Marked pool net asset value in USDT, including derivative positions and cash.',
 'detail':'Human-readable status detail. Do not parse as an enum.', 'error':'Human-readable error; nullable on transfer history.',
 'amount':'Token amount unless the endpoint explicitly states shares.', 'hours':'DCA interval: 1, 24 or 168.',
 'budget':'Optional total USDT DCA spend cap.', 'spent':'Actual cumulative USDT spend.',
 'nextRun':'Next scheduled UTC execution time.', 'lastOutcome':'Human-readable latest execution outcome, nullable before first run.',
 'hash':'On-chain transaction hash, nullable before signing.', 'block':'On-chain block number, nullable before confirmation.',
 'symbol':'Display symbol; perpetual BTCUSDT is distinct from the spot ID BTCUSDT.',
 'strike':'USDT option strike; null for other products.', 'optionSide':'Call or Put; null for other products.',
 'leverage':'Integer leverage 1 through 10, serialized as a string.', 'reduceOnly':'If true, execution must reduce an existing derivative position.',
 'interest':'Accrued debt in the loan asset.', 'principal':'Outstanding principal in the loan asset.', 'badDebt':'Recognized unrecovered debt in the loan asset.',
 'collateral':'Pledged amount in collateralAsset.', 'settlementPrice':'Final settlement index; null before settlement.',
 'iv':'Annualized implied volatility as a fraction, nullable without supported fresh references.',
 'delta':'Option delta, nullable without supported fresh references.', 'mark':'Theoretical USDT option premium per BTC.',
 'received':'Asset amount actually received on liquidity withdrawal; zero while pending.',
 'time':'UTC timestamp; Candle.time is Unix seconds encoded as a string.',
}
def field(t,n):
    nullable=t.endswith('?');t=t.rstrip('?');s={}
    if t in ('decimal','int','long'): s={'type':'string','pattern':r'^-?\d+(\.\d+)?$' if t=='decimal' else r'^-?\d+$'}
    elif t=='Guid':s={'type':'string','format':'uuid'}
    elif t=='DateTime':s={'type':'string','format':'date-time'}
    elif t=='bool':s={'type':'boolean'}
    else:s={'type':'string'}
    if nullable:s['nullable']=True
    s['description']=descriptions.get(n,re.sub(r'(?<!^)([A-Z])',r' \1',n).capitalize()+'.')
    return s
def ref(n):return {'$ref':'#/components/schemas/'+n}
def arr(n):return {'type':'array','items':ref(n)}
def obj(n,p):schemas[n]={'type':'object','properties':p,'required':list(p)};return ref(n)
for name in ['Models.cs','Products.cs','ApiCredential.cs']:
    source=(ROOT/'src/Novcic.Core'/name).read_text(encoding='utf-8')
    for match in re.finditer(r'public class (\w+)\s*\n\{(.*?)\n\}',source,re.S):
        cls,body=match.groups();props={}
        if cls not in ['Wallet','Order','Trade','LedgerEntry','Instrument','Position','RiskEvent','IndexSample','LendingPool','Supply','Loan','LiquidityShare','DcaSchedule','LiquidityOperation']:continue
        for t,n in re.findall(r'public (\w+\??) (\w+) \{ get; set; \}',body):props[n[0].lower()+n[1:]]=field(t,n[0].lower()+n[1:])
        obj(cls,props)
for t,n in [('string','symbol'),('string','baseAsset'),('string','quoteAsset')]:schemas['Instrument']['properties'][n]=field(t,n)
for file in ['Models.cs','Investing.cs','LiquidityEngine.cs','Derivatives.cs']:
    for cls,args in re.findall(r'public record (\w+)\(([^;]+)\);',(ROOT/'src/Novcic.Core'/file).read_text(encoding='utf-8')):
        props={};required=[]
        for item in args.split(','):
            m=re.match(r'\s*(\w+\??)\s+(\w+)(.*)',item)
            if not m:continue
            t,n,default=m.groups();n=n[0].lower()+n[1:];props[n]=field(t,n)
            if not default.strip() and not t.endswith('?'):required.append(n)
        schemas[cls]={'type':'object','properties':props,'required':required}
def fields(items):return {n:field(t,n) for n,t in items.items()}
obj('Error',fields({'error':'string'}));obj('Message',fields({'message':'string'}));obj('Empty',{})
obj('User',fields({'id':'Guid','email':'string','country':'string'}))
obj('Session',dict(fields({'accessToken':'string','expiresAt':'DateTime'}),user=ref('User')))
obj('Login',fields({'email':'string','password':'string'}));obj('Registration',fields({'email':'string','password':'string','country':'string'}))
obj('IdRequest',fields({'id':'Guid'}));obj('CreateKey',fields({'name':'string','scope':'string'}))
obj('ApiKey',fields({'id':'Guid','name':'string','scope':'string','createdAt':'DateTime','expiresAt':'DateTime','revoked':'bool'}))
obj('NewApiKey',fields({'id':'Guid','secret':'string','scope':'string','expiresAt':'DateTime'}))
obj('DepthLevel',fields({'price':'decimal','quantity':'decimal','total':'decimal'}))
obj('PublicTrade',fields({'id':'Guid','price':'decimal','quantity':'decimal','takerSide':'string','createdAt':'DateTime'}))
obj('Market',dict(fields({'symbol':'string','revision':'long','quantityAsset':'string','lastPrice':'decimal?','high':'decimal?','low':'decimal?','volume':'decimal','change':'decimal?','serverTime':'DateTime'}),bids=arr('DepthLevel'),asks=arr('DepthLevel'),trades=arr('PublicTrade')))
obj('Ticker',dict(fields({'instrumentId':'string','lastPrice':'decimal?','change':'decimal?','volume':'decimal'}),bid={'allOf':[ref('DepthLevel')],'nullable':True},ask={'allOf':[ref('DepthLevel')],'nullable':True}))
obj('OptionAnalytics',fields({'instrumentId':'string','iv':'decimal?','mark':'decimal?','delta':'decimal?'}))
obj('Amm',fields({'btc':'decimal','usdt':'decimal','shares':'decimal','feesEarned':'decimal','nav':'decimal?'}))
obj('Products',dict(index={'allOf':[ref('IndexSample')],'nullable':True},priceFresh={'type':'boolean'},instruments=arr('Instrument'),tickers=arr('Ticker'),pools=arr('LendingPool'),amm=ref('Amm'),optionAnalytics=arr('OptionAnalytics')))
obj('Portfolio',dict(risk=ref('RiskSnapshot'),positions=arr('Position'),loans=arr('Loan'),supplies=arr('Supply'),liquidity={'allOf':[ref('LiquidityShare')],'nullable':True},liquidityRequests=arr('LiquidityOperation'),schedules=arr('DcaSchedule'),events=arr('RiskEvent')))
obj('Health',fields({'status':'string'}));obj('Token',fields({'address':'string','decimals':'int'}))
obj('Network',dict(fields({'network':'string','chainId':'long','confirmations':'int','generation':'string'}),tokens={'type':'object','properties':{'BTC':ref('Token'),'USDT':ref('Token')}}))
obj('Address',fields({'address':'string'}))
obj('ChainTransfer',fields({'id':'Guid','kind':'string','asset':'string','amount':'decimal','address':'string','status':'string','hash':'string?','createdAt':'DateTime','block':'long?','error':'string?'}))
obj('Withdrawal',fields({'id':'Guid','userId':'Guid','generation':'string','kind':'string','asset':'string','amount':'decimal','address':'string','status':'string','hash':'string?','rawTransaction':'string?','sender':'string?','nonce':'long?','block':'long?','blockHash':'string?','logIndex':'long','error':'string?','createdAt':'DateTime'}))
schemas['Withdrawal']['properties']['rawTransaction']['description']='Always null in the initial queued response. Use sanitized custody/history for subsequent status; signing data is not exposed there.'
obj('Transfer',fields({'reference':'Guid','message':'string'}));obj('CancelAll',fields({'cancelled':'int'}))
obj('Settlement',fields({'message':'string','amount':'decimal','pending':'decimal'}));schemas['Settlement']['required']=['message']
obj('Config',dict(fields({'symbol':'string','quantityStep':'decimal','priceTick':'decimal','fees':'decimal','feeModel':'string','userToUserFee':'decimal'}),amm=ref('AmmSettings')))
# Supported input enums and precision are visible in generated clients as well as prose.
for cls,n,values in [('PlaceOrder','side',['Buy','Sell']),('PlaceOrder','type',['Limit','Market']),('TransferRequest','from',['Funding','Unified Trading']),('TransferRequest','to',['Funding','Unified Trading']),('CreateKey','scope',['Read','Trade'])]:schemas[cls]['properties'][n]['enum']=values
for cls in ['TransferRequest','WithdrawalRequest','SupplyRequest','BorrowRequest','QueueWithdrawal']:
    schemas[cls]['properties']['asset']['enum']=['BTC','USDT']
schemas['PlaceOrder']['properties']['quantity']['description']='Positive BTC quantity in multiples of 0.000001.'
schemas['PlaceOrder']['properties']['price']['description']='Positive USDT price in multiples of 0.01 for Limit; null for Market.'
schemas['PlaceOrder']['properties']['instrument']['description']='Canonical products.instruments[].id. Default BTCUSDT (spot). Perpetual uses BTC-PERP internally; display symbol BTCUSDT.'
schemas['SupplyRequest']['properties']['amount']['description']='Supply: asset units. Redeem: share units. Positive, up to eight decimal places.'
schemas['RepayRequest']['properties']['amount']['description']='Repay: loan asset amount. Add collateral: collateralAsset amount. Positive, up to eight decimal places.'

# method, path, title, category, authentication, request schema, response schema, behavior, example
endpoints=[]
def ep(method,path,title,group,auth,req,res,note,example=None):endpoints.append(dict(method=method,path='/api/'+path,title=title,group=group,auth=auth,request=req,response=res,note=note,example=example))
ep('GET','health','Service health','public','Public',None,'Health','Returns ready when the API can reach its database. This does not guarantee fresh market references.')
ep('GET','config','Trading configuration','public','Public',None,'Config','Read configured ticks and AMM fees. Legacy fees is user-to-user only; use amm for execution fees. DerivativeLevels is the maximum per-side target before tick collisions and risk constraints.')
ep('GET','network','Supported network and tokens','public','Public',None,'Network','Read current generation and contract addresses before depositing. The currently implemented network is Ethereum Local, chain ID 31337, with three confirmations. BTC is an ERC-20 token here, not native Bitcoin.')
ep('GET','market','Aggregated order book','public','Public',None,'Market','instrument defaults to BTCUSDT. Bids descend, asks ascend. Each row is one aggregated price level, with cumulative BTC total. No individual public orders. Latest 100 trades; market high/low/volume/change use executed exchange trades over 24 hours.')
ep('GET','products','Instruments, tickers and pools','public','Public',None,'Products','Discover canonical IDs and status before trading. Perpetual symbol BTCUSDT maps to ID BTC-PERP. Last price and change may be null. IV and option marks are null when references are unsupported or stale. Index freshness threshold is five seconds.')
ep('GET','candles','Trade candles','public','Public',None,'Candle[]','instrument defaults to BTCUSDT; interval is 1m, 5m, 15m or 1h. Up to 500 candles. time is Unix seconds as a string. Candles derive from exchange executions, not external index prices; quiet periods may carry forward the last known close.')
ep('POST','auth/register','Create account','sessions','Public','Registration','Session','Email is normalized to lowercase. Password requires 10-128 characters including a letter and digit. Country is uppercase ISO alpha-2. Returns a 10-minute access token and sets a rotating 7-day HttpOnly refresh cookie.',{'email':'trader@example.com','password':'replace-with-a-strong-password-123','country':'RS'})
ep('POST','auth/login','Sign in','sessions','Public','Login','Session','Returns access token and refresh cookie. Invalid credentials return 401. Registration, login and refresh share a fixed-window limit of 30 requests per minute per IP.',{'email':'trader@example.com','password':'your-password'})
ep('POST','auth/refresh','Rotate session','sessions','Refresh cookie','Empty','Session','Send novcic_refresh cookie. Rotation invalidates the old token. Reuse revokes its entire session family. Serialize refresh attempts per session.',{})
ep('POST','auth/logout','Sign out','sessions','Refresh cookie','Empty','Message','Revokes the refresh session family, if present, and clears its cookie. Already-issued access tokens expire normally.',{})
ep('GET','auth/me','Current account','sessions','Read',None,'User','Returns only account ID, email and country.')
ep('GET','balances','Wallet balances','account','Read',None,'Wallet[]','Returns BTC and USDT wallets for Funding and Unified Trading. available plus locked is wallet total. Use risk.transferable values before transferring collateral out.')
ep('GET','orders','Account orders','trading','Read',None,'Order[]','Latest 200 account orders across instruments, descending sequence. Includes closed orders. No cursor, offset or history export endpoint is implemented.')
ep('GET','trades','Account executions','trading','Read',None,'Trade[]','Latest 200 account trades across instruments, descending sequence. buyerId and sellerId determine your side. Public /market strips counterparty identity.')
ep('GET','ledger','Account ledger','account','Read',None,'LedgerEntry[]','Latest 100 wallet movements, descending ledger ID. Signed deltas are exact decimal strings. Use referenceId to associate movements with operations; historical pagination is not implemented.')
ep('GET','portfolio','Risk and product positions','account','Read',None,'Portfolio','A consistent account snapshot. Includes all positions, loans, supplies and DCA schedules; latest 100 risk events and liquidity requests. Funding balances are outside cross-margin equity.')
ep('POST','transfer','Transfer between accounts','account','JWT','TransferRequest','Transfer','Atomic wallet transfer. From and to must differ. Unified Trading outflows respect margin and option coverage; Funding-to-Trading top-ups remain possible during stale prices.',{'asset':'USDT','from':'Funding','to':'Unified Trading','amount':'100.00'})
ep('POST','orders','Place order','trading','Trade','PlaceOrder','Order','Price-time matching on the canonical instrument. A successful HTTP response is not a guarantee of a full fill: inspect status, remaining and cancelReason. Market remainders cancel; no fabricated liquidity. Self-trade prevention cancels the incoming remainder.',{'side':'Buy','type':'Limit','quantity':'0.001000','price':'75000.00','instrument':'BTCUSDT','leverage':'1','reduceOnly':False})
ep('POST','orders/{id}/cancel','Cancel order','trading','Trade','Empty','Order','Only your own open or partially-filled order may be cancelled. A new cancel intent for a closed order returns 400. Replaying the same successful command key returns its original receipt. A fill may win the race before cancellation.',{})
ep('POST','cancel-all','Cancel all open orders','trading','Trade','Empty','CancelAll','Cancels your open and partially-filled orders across every instrument and releases reserved spot funds.',{})
ep('POST','settle-cash','Settle derivative cash','account','JWT','Empty','Settlement','Transfers realized USDT claims from funded clearing liquidity. Can settle partially; pending remains in risk.realizedCash. No cash is created to satisfy an unfunded profit claim.',{})
ep('POST','custody/address','Deposit address','custody','JWT','Empty','Address','Returns the account deposit address, creating it once if needed. The same address is used for supported ERC-20 tokens. This route is naturally idempotent and does not use command receipts.',{})
ep('GET','custody/history','Deposit and withdrawal history','custody','Read',None,'ChainTransfer[]','Latest 100 deposits and withdrawals. Private signing keys, raw transactions and internal sweep/gas jobs are excluded. Poll this endpoint for finality.')
ep('POST','withdraw','Request withdrawal','custody','JWT','WithdrawalRequest','Withdrawal','Debits Funding atomically and queues signing/broadcast. BTC supports eight decimals; USDT six. Destination must be a nonzero 0x address with 40 hexadecimal digits. Final status appears in custody/history.',{'asset':'USDT','address':'0x1111111111111111111111111111111111111111','amount':'10.000000'})
ep('GET','keys','List API keys','keys','JWT',None,'ApiKey[]','Lists key metadata including expired/revoked keys. Secrets are never returned here. Signed API keys cannot manage keys.')
ep('POST','keys','Create API key','keys','JWT','CreateKey','NewApiKey','Name is required, maximum 60 characters. Scope is Read or Trade. Maximum ten unexpired active keys. Expiry is 90 days. Store secret from this one response; a retry creates another key.',{'name':'Trading integration','scope':'Trade'})
ep('POST','keys/{id}/revoke','Revoke API key','keys','JWT','Empty','Message','Revokes a key owned by the session account. Returns success even when no matching owned key exists. Naturally idempotent.',{})
ep('POST','supply','Subscribe to Earn','earn','JWT','SupplyRequest','Supply','Moves Unified Trading funds to the lending pool and issues shares at the current pool unit value. Interest accrues before subscription.',{'asset':'USDT','amount':'100.00000000'})
ep('POST','redeem','Redeem Earn shares','earn','JWT','SupplyRequest','Supply','amount means shares, not token amount. Queues shares and pays immediately to the extent that pool cash is available. Remaining queuedShares settle as liquidity returns.',{'asset':'USDT','amount':'100.00000000'})
ep('POST','borrow','Create collateralized loan','earn','JWT','BorrowRequest','Loan','Requires the other asset as collateral, fresh index, available pool cash, and initial LTV at most 60%. Interest is variable with utilization.',{'asset':'USDT','amount':'100.00000000','collateralAsset':'BTC','collateral':'0.01000000'})
ep('POST','repay','Repay loan','earn','JWT','RepayRequest','Loan','Pays accrued interest before principal. Requested amount is capped at total debt; proportional collateral returns. Full repayment marks Repaid.',{'loanId':'11111111-1111-4111-8111-111111111111','amount':'100.00000000'})
ep('POST','add-collateral','Add loan collateral','earn','JWT','RepayRequest','Loan','amount is in the existing loan collateralAsset. Only active loans owned by the account may be changed.',{'loanId':'11111111-1111-4111-8111-111111111111','amount':'0.00100000'})
ep('POST','liquidity-deposit','Queue liquidity contribution','liquidity','JWT','QueueDeposit','LiquidityOperation','Accepts either or both assets into escrow. An unbalanced contribution waits for real spot counterparties to reach approximately 50:50 value. Shares are issued only on activation at current NAV.',{'btc':'0.01000000','usdt':'750.00000000'})
ep('POST','liquidity-withdraw','Queue liquidity redemption','liquidity','JWT','QueueWithdrawal','LiquidityOperation','Locks unqueued shares. Specify BTC or USDT for proceeds. Redemption waits for chosen-asset liquidity and collateral headroom; share value changes until execution.',{'shares':'100.00000000','asset':'USDT'})
ep('POST','liquidity-cancel','Cancel pending liquidity request','liquidity','JWT','IdRequest','LiquidityOperation','Only Pending requests can be cancelled. Deposit cancellation returns remaining escrow assets; completed conversions remain final. Withdrawal cancellation unlocks shares.',{'id':'11111111-1111-4111-8111-111111111111'})
ep('POST','dca','Create recurring purchase','automation','JWT','DcaRequest','DcaSchedule','amount is positive USDT per purchase, up to six decimals. hours is 1, 24 or 168. Optional budget must cover at least one run. First run is scheduled one interval ahead; funds are not reserved.',{'amount':'10.000000','hours':'24','budget':'100.000000'})
ep('POST','toggle-dca','Pause or resume purchase plan','automation','JWT','IdRequest','DcaSchedule','Toggles paused state. Resume schedules a new run one interval from now. Reuse the same idempotency key when retrying to avoid toggling twice.',{'id':'11111111-1111-4111-8111-111111111111'})
for path in ['add-liquidity','remove-liquidity','swap']:ep('POST',path,'Retired '+path,'retired','JWT','Empty','Error','Returns 410 Gone. Use liquidity-deposit/liquidity-withdraw, or place ordinary spot orders against the integrated AMM.',{})
ep('POST','faucet','Disabled development faucet','retired','JWT','Empty','Message','Disabled in the configured exchange; returns 404. Local test funding is an operator CLI operation, not a public funding API.',{})

spec={'openapi':'3.0.3','info':{'title':'Nov\u010di\u0107 Public API','version':'1.0.0','description':'Implemented public REST interface. Amounts and integer values are serialized as strings. No production base URL is announced.'},'servers':[{'url':'http://127.0.0.1:8081','description':'Local exchange'}],'paths':{},'components':{'schemas':schemas,'securitySchemes':{'Bearer':{'type':'http','scheme':'bearer','bearerFormat':'JWT'},'SignedKey':{'type':'apiKey','in':'header','name':'X-Api-Key','description':'Requires X-Api-Timestamp, X-Api-Nonce and X-Api-Signature. See authentication guide.'},'RefreshCookie':{'type':'apiKey','in':'cookie','name':'novcic_refresh'}}}}
for e in endpoints:
    name=e['response'];response=arr(name[:-2]) if name.endswith('[]') else ref(name)
    status='410' if e['path'].split('/')[-1] in ['add-liquidity','remove-liquidity','swap'] else '404' if e['path']=='/api/faucet' else '200'
    op={'summary':e['title'],'description':e['note'],'operationId':e['method'].lower()+'_'+re.sub(r'[^a-zA-Z0-9]+','_',e['path']).strip('_'),'tags':[e['group']],'responses':{status:{'description':e['note'],'content':{'application/json':{'schema':response}}}},'security':[],'parameters':[]}
    if e['auth']=='JWT':op['security']=[{'Bearer':[]}]
    if e['auth'] in ['Read','Trade']:op['security']=[{'Bearer':[]},{'SignedKey':[]}]
    if e['auth']=='Refresh cookie':op['security']=[{'RefreshCookie':[]}]
    e['idempotent']=e['method']=='POST' and e['group'] not in ['sessions','keys','retired'] and e['path']!='/api/custody/address'
    if e['idempotent']:op['parameters'].append({'name':'Idempotency-Key','in':'header','required':True,'schema':{'type':'string','format':'uuid'},'description':'Reuse unchanged for retries of this operation.'})
    if '{id}' in e['path']:op['parameters'].append({'name':'id','in':'path','required':True,'schema':{'type':'string','format':'uuid'},'description':'Resource UUID.'})
    if e['path'] in ['/api/market','/api/candles']:op['parameters'].append({'name':'instrument','in':'query','required':False,'schema':{'type':'string','default':'BTCUSDT'}})
    if e['path']=='/api/candles':op['parameters'].append({'name':'interval','in':'query','required':False,'schema':{'type':'string','enum':['1m','5m','15m','1h'],'default':'1m'}})
    if e['request']:op['requestBody']={'required':True,'content':{'application/json':{'schema':ref(e['request']),'example':e['example']}}}
    for code in ['400','401','403','429','503']:op['responses'].setdefault(code,{'description':{'400':'Validation or domain rule error.','401':'Authentication failed; body may be empty.','403':'Origin rejected.','429':'Authentication rate limit; body may be empty.','503':'Downstream unavailable; outcome may be ambiguous.'}[code]})
    spec['paths'].setdefault(e['path'],{})[e['method'].lower()]=op
sources={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes().replace(b'\r\n',b'\n')).hexdigest() for folder in ['src/Novcic.Api','src/Novcic.Matching','src/Novcic.Core','src/Novcic.Custody'] for p in (ROOT/folder).glob('*.cs')}
for file,data in [('openapi.json',spec),('endpoints.json',endpoints),('source-manifest.json',{'version':'1.0.0','files':sources})]:(OUT/file).write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(f'Exported {len(endpoints)} operations and {len(schemas)} schemas.')
