import type {useCommand} from './hooks';
export default function CommandFeedback({command}:{command:ReturnType<typeof useCommand>}){
  return command.canRetry?<div className="recovery-notice" role="status">A request needs reconciliation. <button type="button" disabled={command.busy} onClick={()=>void command.retry()}>Retry original request</button></div>:null;
}
