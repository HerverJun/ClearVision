from pathlib import Path

import onnx
from onnx import TensorProto, helper, numpy_helper
import numpy as np


output_path = Path(__file__).with_name("classification_color_mean_2x2.onnx")

input_tensor = helper.make_tensor_value_info(
    "images", TensorProto.FLOAT, [1, 3, 2, 2]
)
output_tensor = helper.make_tensor_value_info(
    "probabilities", TensorProto.FLOAT, [1, 3]
)

scale = numpy_helper.from_array(np.array([10.0], dtype=np.float32), name="scale")
nodes = [
    helper.make_node(
        "ReduceMean", ["images"], ["channel_means"], axes=[2, 3], keepdims=0
    ),
    helper.make_node("Mul", ["channel_means", "scale"], ["logits"]),
    helper.make_node("Softmax", ["logits"], ["probabilities"], axis=1),
]

graph = helper.make_graph(
    nodes,
    "classification_color_mean_2x2",
    [input_tensor],
    [output_tensor],
    initializer=[scale],
)
model = helper.make_model(
    graph,
    producer_name="ClearVision deterministic test fixture",
    opset_imports=[helper.make_operatorsetid("", 11)],
)
model.ir_version = 7
metadata = model.metadata_props.add()
metadata.key = "names"
metadata.value = '["red", "green", "blue"]'
onnx.checker.check_model(model)
onnx.save(model, output_path)
print(output_path)
