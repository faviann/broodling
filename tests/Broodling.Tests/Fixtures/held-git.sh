#!/bin/sh
# Controlled local Git boundary; product still executes real Git.
case " $* " in
  *" worktree ${BROODLING_WITNESS_OPERATION:-add} "*)
    if [ "$BROODLING_WITNESS_MODE" = after ]; then
      "$BROODLING_WITNESS_REAL_GIT" "$@" > "$BROODLING_WITNESS_ENTERED.git-log" 2>&1 || exit $?
    fi
    printf '%s\n' "$$" > "$BROODLING_WITNESS_ENTERED"
    count=0
    while [ ! -e "$BROODLING_WITNESS_GATE" ]; do
      sleep 0.01
      count=$((count + 1))
      [ "$count" -lt 2500 ] || exit 98
    done
    [ "$BROODLING_WITNESS_MODE" != after ] || exit 0
    # Permit the orphan to finish even when its caller's output reader dies.
    # This changes no product signal policy.
    exec "$BROODLING_WITNESS_REAL_GIT" "$@" > "$BROODLING_WITNESS_ENTERED.git-log" 2>&1
    ;;
esac
exec "$BROODLING_WITNESS_REAL_GIT" "$@"
