export const AI_TASK_CONTRACT_VERSION = 'v1';

export const AI_PRIMARY_TASKS = Object.freeze([
    Object.freeze({ canonical: 'presence_absence', aliases: Object.freeze(['presence', 'presence_detection']) }),
    Object.freeze({ canonical: 'attribute_classification', aliases: Object.freeze(['classification', 'attribute', 'image_classification']) }),
    Object.freeze({ canonical: 'object_detection', aliases: Object.freeze(['target_detection']) }),
    Object.freeze({ canonical: 'template_location', aliases: Object.freeze(['template_matching', 'template_match', 'template_positioning']) }),
    Object.freeze({ canonical: 'surface_defect', aliases: Object.freeze(['surface_or_pose_defect', 'surface_defect_detection']) }),
    Object.freeze({ canonical: 'geometry_measurement', aliases: Object.freeze(['measurement', 'measure']) }),
    Object.freeze({ canonical: 'wire_sequence', aliases: Object.freeze(['sequence', 'sequence_judgment']) }),
    Object.freeze({ canonical: 'code_recognition', aliases: Object.freeze(['barcode_qr', 'ocr']) })
]);

const TASK_BY_VALUE = new Map();
AI_PRIMARY_TASKS.forEach(task => {
    [task.canonical, ...task.aliases].forEach(value => TASK_BY_VALUE.set(value, task.canonical));
});

export function normalizeAiPrimaryTask(value) {
    return TASK_BY_VALUE.get(String(value || '').trim().toLowerCase()) || '';
}

export const PLAN_ANSWER_ORIGIN_PRIORITY = Object.freeze({
    explicit_user_text: 6,
    explicit_user_selection: 6,
    resource_bound: 5,
    model_inferred: 4,
    accepted_recommended_default: 3,
    rule_inferred: 2,
    legacy_inferred: 2,
    default_assumption: 1
});

export function planAnswerOriginPriority(origin) {
    return PLAN_ANSWER_ORIGIN_PRIORITY[String(origin || '').trim().toLowerCase()] || 0;
}
