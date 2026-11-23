-- The list of things to remember: one row per entry, owned by one person.
create table items (
  id          uuid primary key,
  user_id     uuid not null references users (id) on delete cascade,
  title       text not null,
  note        text not null default '',
  done        boolean not null default false,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);
-- ids are UUID version 7 (sorted by time): the list reads the newest first along this index
create index items_user_idx on items (user_id, id desc);
