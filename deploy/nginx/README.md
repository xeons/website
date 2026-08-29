# Running behind an existing nginx

Use this when the server already has nginx on ports 80 and 443. Caddy is not involved,
and the two must never both try to bind those ports.

## 1. Start the stack without Caddy

Caddy sits behind a compose profile, so leaving the profile off simply does not start it:

```bash
docker compose up -d --build
```

The app then publishes on `127.0.0.1:8080`. Binding to loopback rather than `0.0.0.0`
means nothing outside the machine can reach it directly, only through nginx.

To use Caddy instead, on a server that has no other web server:

```bash
docker compose --profile caddy up -d --build
```

## 2. Install the server block

`xeonproductions.conf` holds two server blocks: the canonical host, which proxies to the
application, and every other name the site answers to, which redirects to it. One file is
enough; nginx places no limit on server blocks per file.

```bash
sudo cp deploy/nginx/xeonproductions.conf /etc/nginx/sites-available/xeonproductions
sudo ln -s /etc/nginx/sites-available/xeonproductions /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

## 3. The websocket map

Blazor Server needs the connection upgrade. Add this once, at the `http` level, in
`/etc/nginx/nginx.conf` if it is not already there:

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}
```

Without it, `$connection_upgrade` is empty, the websocket never establishes, and every
interactive admin screen stops responding to clicks with no error shown.

## 4. Certificates

```bash
sudo certbot --nginx   -d xeonproductions.com -d www.xeonproductions.com   -d xeonproductions.net -d www.xeonproductions.net   -d xeonproductions.dev -d www.xeonproductions.dev   -d xeons.net           -d www.xeons.net
```

Every name has to resolve to this server first; one failure aborts the whole run. One
certificate then covers both server blocks.

certbot fills in the `ssl_certificate` lines and adds the http-to-https redirect.
