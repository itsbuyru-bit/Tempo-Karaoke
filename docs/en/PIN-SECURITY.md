English | [Русский](../ru/PIN-SECURITY.md)

# Persistent Host and Admin PIN

## Behaviour

By default the remote generates a random four-digit PIN for the host and for the
administrator on every remote session. Settings can store a permanent PIN for
either role:

**Settings → Security → Access PIN codes → Permanent PIN**.

* When the option is off, nothing changes: random PINs are shown in the remote
  page.
* When it is on, the remote verifies the entered PIN against the stored hash and
  shows `••••` instead of the PIN on the desktop.
* A permanent PIN is 4–8 digits, requires confirmation on save, and must not be
  empty. Common values such as `1234` are accepted but the settings show a
  warning.
* Reset asks for confirmation and returns to random PINs.

## Storage

`settings.json`:

```json
{
  "Security": {
    "UsePersistentPin": true,
    "HostPin": {
      "Algorithm": "PBKDF2-SHA256",
      "Salt": "<base64>",
      "Hash": "<base64>",
      "Iterations": 100000
    },
    "AdminPin": {
      "Algorithm": "PBKDF2-SHA256",
      "Salt": "<base64>",
      "Hash": "<base64>",
      "Iterations": 100000
    }
  }
}
```

The plaintext PIN is never written to disk, logs or reports. Each record has its
own 16-byte cryptographically random salt and a 32-byte derived key; verification
uses `CryptographicOperations.FixedTimeEquals`. Settings are read live, so a new
PIN applies without restarting the remote server.

## Verification and policy

`PinAuthentication.Verify`:

1. A role with `UsePersistentPin == true` and a usable `Salt`/`Hash` record is
   verified with PBKDF2-SHA256.
2. `UsePersistentPin == false` keeps the old random-PIN comparison.
3. If the setting is enabled but a role has no usable record, that role falls
   back to its random PIN and the settings page shows
   “Permanent PIN is enabled but missing for: …”. This avoids locking the host
   out while still making the misconfiguration visible. (Rejected alternative:
   refusing sign-in until a PIN is configured.)

Remote server rules:

* `/api/auth` checks the admin PIN quietly first, then the host PIN. A successful
  host sign-in does not consume administrator attempts.
* Five failed attempts per role trigger a 5-minute lockout; the counter survives
  cookie removal.
* Failures are logged with the role and attempt number only. The PIN and its hash
  are never logged.

## Migration

Existing `settings.json` files need no manual migration: the missing `Security`
block deserializes as disabled. Resetting writes an empty block.

## Tests

`PinSecurityTests` covers:

* correct / incorrect PIN;
* PBKDF2 parameters, unique salts and unique hashes;
* no plaintext PIN in serialized settings;
* 4–8 digit policy;
* persistent vs random selection;
* missing role record → random fallback + configuration error;
* no verification against an empty configuration.

See also: [`DIAGNOSTICS.md`](DIAGNOSTICS.md) for the report/log privacy rules.
