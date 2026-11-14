
import sys
from chessimg2pos import predict_fen

# Keep the Python process alive and process image paths one at a time.
for image_path in sys.stdin:
    image_path = image_path.strip()

    if not image_path:
        continue

    try:
        result = predict_fen(image_path)
        print(result, flush=True)
    except Exception as e:
        print(f"ERROR: {e}", flush=True)
