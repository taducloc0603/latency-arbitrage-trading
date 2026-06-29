-- Supabase config table for the latency-arb tool.
-- Each running PC loads the most recent active row matching its hostname.

create table if not exists public.configs (
  id                    uuid primary key default gen_random_uuid(),
  group_name            text not null,
  hostname              text not null,             -- matched against Environment.MachineName
  point                 int  not null default 100, -- price -> point multiplier
  open_pts              int  not null,             -- x: final gap must reach this to fire
  open_hold_confirm_ms  int  not null,             -- y: gap must hold the sustain floor this long
  open_confirm_gap_pts  int  not null,             -- z: sustain floor across the whole confirm window
  stop_loss_point       int  not null,             -- SL distance (used while trailing not active)
  trailing_start_point  int  not null,             -- profit distance that activates trailing
  trailing_step_point   int  not null,             -- trailing give-back from peak/trough
  map_a                 text not null default 'Local\MT_A_Tick',
  map_b                 text not null default 'Local\MT_B_Tick',
  chart_hwnd_b          text,                       -- HWND of B chart (click buy/sell)
  trade_hwnd_b          text,                       -- HWND of B trade panel (close row 0)
  is_active             boolean not null default true,
  created_at            timestamptz not null default now()
);

create index if not exists configs_hostname_active_idx
  on public.configs (hostname, is_active, created_at desc);

-- If RLS is enabled, allow anon to read and to update (for the app's "Save Config"
-- write-back of HWND / map names). Do NOT grant insert/delete to anon.
-- create policy "anon read configs"   on public.configs for select to anon using (true);
-- create policy "anon update configs" on public.configs for update to anon using (true) with check (true);

-- Example row
insert into public.configs
  (group_name, hostname, point, open_pts, open_hold_confirm_ms, open_confirm_gap_pts,
   stop_loss_point, trailing_start_point, trailing_step_point,
   map_a, map_b, chart_hwnd_b, trade_hwnd_b)
values
  ('LAP 2', 'desktop-ndpzoz8', 100, 80, 1000, 30,
   50, 200, 30,
   'Local\MT_A_Tick', 'Local\MT_B_Tick', '0x00180070', '0x0085089A');
