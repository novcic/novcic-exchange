import { useEffect, useRef, type ReactNode } from 'react';
export default function Modal({ title, children, close }: { title: string; children: ReactNode; close: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { const dialog = ref.current!; dialog.showModal(); return () => dialog.close(); }, []);
  return <dialog ref={ref} className="modal" onCancel={e => { e.preventDefault(); close(); }} onClick={e => { if (e.target === ref.current) close(); }}>
    <div className="panel-heading"><h2>{title}</h2><button aria-label="Close dialog" className="text-button" onClick={close}>✕</button></div><div className="modal-content">{children}</div>
  </dialog>;
}
