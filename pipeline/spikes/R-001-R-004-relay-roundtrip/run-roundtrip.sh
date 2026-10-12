#!/usr/bin/env bash
# R-001 two-process round trip with a mid-game kill/restart of B, then a cold replay of B's view.
# Usage: ./run-roundtrip.sh <label> <relay-url[,relay-url]> [kind=9999] [turns=12]
# Publishes (turns) signed events (NIP-44 v2 encrypted content, throwaway keys, ~600-900 bytes each) to the given relay(s) plus one REQ per (re)connect.
set -u
cd "$(dirname "$0")"
LABEL=${1:?label}; RELAY=${2:?relay}; KIND=${3:-9999}; TURNS=${4:-12}
D=out/$LABEL; rm -rf "$D"; mkdir -p "$D"
P="dotnet peer-bin/Peer.dll"
PA=$($P --gen-key $D/a.key); PB=$($P --gen-key $D/b.key)
GAME=spike-$(head -c 6 /dev/urandom | od -An -tx1 | tr -d ' \n')
echo "game=$GAME A=$PA B=$PB relay=$RELAY kind=$KIND turns=$TURNS" | tee $D/run.txt
SINCE=$(( $(date +%s) - 60 ))
$P --name A --role first  --key $D/a.key --peer $PB --game $GAME --relay $RELAY --kind $KIND --turns $TURNS --state $D/a.state --log $D/a.log --since $SINCE --timeout-s 150 > $D/a.out 2>&1 &
APID=$!
$P --name B --role second --key $D/b.key --peer $PA --game $GAME --relay $RELAY --kind $KIND --turns $TURNS --state $D/b.state --log $D/b.log --since $SINCE --die-after-publish 4 --timeout-s 150 > $D/b1.out 2>&1
echo "B first run exited with code $? (expected non-zero: killed after publishing turn 4)" | tee -a $D/run.txt
sleep 6   # A publishes turn 5 while B is dead; B cannot see it live
echo "--- restarting B (same key, same state file) at $(date +%T)" | tee -a $D/run.txt
$P --name B --role second --key $D/b.key --peer $PA --game $GAME --relay $RELAY --kind $KIND --turns $TURNS --state $D/b.state --log $D/b.log --since $SINCE --timeout-s 150 > $D/b2.out 2>&1
echo "B second run exit $?" | tee -a $D/run.txt
wait $APID; echo "A exit $?" | tee -a $D/run.txt
echo "--- cold replay of B's view (fresh process, B's key, NO state file)" | tee -a $D/run.txt
$P --name B-replay --role second --key $D/b.key --peer $PA --game $GAME --relay $RELAY --kind $KIND --turns $TURNS --replay --log $D/replay.log --since $SINCE --timeout-s 60 > $D/replay.out 2>&1
echo "replay exit $?" | tee -a $D/run.txt
grep -h '"ev":"DONE"' $D/a.out $D/b2.out $D/replay.out | tee -a $D/run.txt
