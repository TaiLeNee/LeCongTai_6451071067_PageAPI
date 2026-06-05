#!/bin/bash
# Replay messages from dead_letter topic back to send_retry for reprocessing.
#
# Usage:
#   ./replay-dead-letter.sh [options]
#
# Options:
#   --dry-run                 Print messages only, do NOT republish (default: false)
#   --max-messages N          Max number of messages to replay   (default: 10)
#   --topic NAME              Source topic                       (default: dead_letter)
#   --target NAME             Target topic                       (default: send_retry)
#   --bootstrap-server HOST   Kafka bootstrap server             (default: $KAFKA_BOOTSTRAP_SERVERS or localhost:9092)
#   -h, --help                Show this help and exit
#
# Environment:
#   KAFKA_BOOTSTRAP_SERVERS   Override the default bootstrap server
#
# Examples:
#   ./replay-dead-letter.sh
#   ./replay-dead-letter.sh --dry-run --max-messages 5
#   ./replay-dead-letter.sh --topic dead_letter --target send_retry --max-messages 50
#
# Docker (run inside Kafka container or from host with port mapping):
#   docker exec -it fb_api-kafka-1 bash -c "
#     KAFKA_BOOTSTRAP_SERVERS=localhost:9092 ./replay-dead-letter.sh --dry-run
#   "

set -euo pipefail

BOOTSTRAP_SERVER="${KAFKA_BOOTSTRAP_SERVERS:-localhost:9092}"
TOPIC="dead_letter"
TARGET_TOPIC="send_retry"
DRY_RUN=false
MAX_MESSAGES=10

while [[ $# -gt 0 ]]; do
  case $1 in
    --dry-run)       DRY_RUN=true; shift ;;
    --max-messages)  MAX_MESSAGES="$2"; shift 2 ;;
    --topic)         TOPIC="$2"; shift 2 ;;
    --target)        TARGET_TOPIC="$2"; shift 2 ;;
    --bootstrap-server) BOOTSTRAP_SERVER="$2"; shift 2 ;;
    -h|--help)
      sed -n '2,20p' "$0" | sed 's/^#//' | sed 's/^ //'
      exit 0 ;;
    *)
      echo "ERROR: Unknown option '$1'. Use --help for usage." >&2
      exit 1 ;;
  esac
done

if ! command -v kafka-console-consumer &>/dev/null; then
  echo "ERROR: kafka-console-consumer not found in PATH." >&2
  echo "HINT: Run this script inside the Kafka container, or install the Confluent CLI tools." >&2
  echo "      docker exec -it fb_api-kafka-1 /bin/bash" >&2
  exit 1
fi

if ! command -v kafka-console-producer &>/dev/null; then
  echo "ERROR: kafka-console-producer not found in PATH." >&2
  echo "HINT: Run this script inside the Kafka container, or install the Confluent CLI tools." >&2
  echo "      docker exec -it fb_api-kafka-1 /bin/bash" >&2
  exit 1
fi

if ! [[ "$MAX_MESSAGES" =~ ^[0-9]+$ ]]; then
  echo "ERROR: --max-messages must be a positive integer, got '$MAX_MESSAGES'" >&2
  exit 1
fi

cat <<EOF
=== Dead Letter Replay Tool ===
  Source topic:   $TOPIC
  Target topic:   $TARGET_TOPIC
  Bootstrap:      $BOOTSTRAP_SERVER
  Max messages:   $MAX_MESSAGES
  Dry run:        $DRY_RUN
EOF

TMPFILE=$(mktemp /tmp/dead-letter-replay-XXXXXX)
trap 'rm -f "$TMPFILE"' EXIT

echo ""
echo "Consuming up to $MAX_MESSAGES messages from '$TOPIC'..."

# Consume messages to a temp file.
# --from-beginning ensures we read existing (not just future) messages.
# --timeout-ms 3000 makes the consumer exit if no messages arrive within 3s.
# stderr is silenced to avoid group-coordination noise.
set +e
kafka-console-consumer \
  --bootstrap-server "$BOOTSTRAP_SERVER" \
  --topic "$TOPIC" \
  --from-beginning \
  --max-messages "$MAX_MESSAGES" \
  --timeout-ms 3000 \
  > "$TMPFILE" \
  2>/dev/null
CONSUMER_EXIT=$?
set -e

# Exit code 0 = consumed exactly max-messages, 1 = nothing left / timed out.
# Both are valid outcomes.
MSG_COUNT=$(wc -l < "$TMPFILE" | tr -d ' ')
echo "Consumed $MSG_COUNT message(s)."

if [[ "$MSG_COUNT" -eq 0 ]]; then
  echo "Nothing to replay. Exiting."
  exit 0
fi

if [[ "$DRY_RUN" == true ]]; then
  echo ""
  echo "=== DRY RUN — messages would be sent to '$TARGET_TOPIC' ==="
  cat "$TMPFILE"
  echo ""
  echo "($MSG_COUNT message(s) would have been replayed)"
else
  echo ""
  echo "Publishing $MSG_COUNT message(s) to '$TARGET_TOPIC'..."

  cat "$TMPFILE" | kafka-console-producer \
    --bootstrap-server "$BOOTSTRAP_SERVER" \
    --topic "$TARGET_TOPIC"

  echo "Done. $MSG_COUNT message(s) replayed from '$TOPIC' → '$TARGET_TOPIC'."
fi
