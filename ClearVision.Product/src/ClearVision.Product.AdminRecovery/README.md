# ClearVision local Admin recovery

This console executable is the only break-glass path for restoring Admin authority. It does not
start the Desktop web server and has no HTTP route. Stop ClearVision Desktop before using it.

Recovery is default-off and requires all of the following in the same local console process:

1. `CLEARVISION_ENABLE_LOCAL_ADMIN_RECOVERY=1`
2. an explicit absolute database path on a local drive
3. `--confirm RECOVER_LOCAL_ADMIN`
4. a username and a new password entered twice without command-line or environment exposure

The operation creates or restores that named user as an active Admin, resets its password, and only
sets the installation latch to completed. It never reopens anonymous setup.
