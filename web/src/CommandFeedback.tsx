import type {useCommand} from './hooks';
export default function CommandFeedback({command}:{command:ReturnType<typeof useCommand>}){
  return <>{(command.error||command.canRetry)&&<p className="error" role="alert">{command.error||'A previous request needs reconciliation.'}{command.canRetry&&<button type="button" className="text-button" disabled={command.busy} onClick={()=>void command.retry()}>Retry original request</button>}</p>}{command.notice&&<p className="notice" role="status">{command.notice}</p>}</>;
}
