import json
import sys

with open('算子资料/算子目录.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

c_ops = [op for op in data['operators'] if op['quality']['level'] == 'C']
for op in c_ops:
    q = op['quality']
    print("{}|{}|{}|{}|{}|{}|{}|{}".format(
        op['id'],
        q['totalScore'],
        q['documentationScore'],
        q['testCoverageScore'],
        q['parameterValidationScore'],
        q['errorHandlingScore'],
        op['category'],
        op.get('version', '?')
    ))
print('Total C-level: {}'.format(len(c_ops)))
