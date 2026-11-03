export function Chevron({open=false}:{open?:boolean}) {
  return <svg className={'nav-chevron'+(open?' is-open':'')} viewBox="0 0 20 20" aria-hidden="true" focusable="false"><path d="m5 7.5 5 5 5-5"/></svg>;
}

export function ProductIcon({kind}:{kind:string}) {
  const paths:Record<string,string>={
    Spot:'M4 8h15m-4-4 4 4-4 4M20 16H5m4-4-4 4 4 4',
    Perpetual:'M12 12c-2-5-8-5-8 0s6 5 8 0 8-5 8 0-6 5-8 0',
    Future:'M5 5h14v15H5ZM8 3v4m8-4v4M5 10h14m-10 4h6',
    Option:'M4 20V4m0 16h16M7 16l5-5 6 5m-6-5V6'
  };
  return <svg className="product-menu-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d={paths[kind]}/></svg>;
}
